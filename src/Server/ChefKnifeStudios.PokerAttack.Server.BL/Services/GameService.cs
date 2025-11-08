using ChefKnifeStudios.PokerAttack.Server.Core.Interfaces;
using ChefKnifeStudios.PokerAttack.Server.Core.Models;
using ChefKnifeStudios.PokerAttack.Server.Data.Models;
using ChefKnifeStudios.PokerAttack.Server.Data.Repos;
using ChefKnifeStudios.PokerAttack.Server.Data.Specifications;
using ChefKnifeStudios.PokerAttack.Shared;
using ChefKnifeStudios.PokerAttack.Shared.DTOs.Gameplay;
using ChefKnifeStudios.PokerAttack.Shared.DTOs.SignalR;
using ChefKnifeStudios.PokerAttack.Shared.DTOs.SignalR.EventArgs;
using ChefKnifeStudios.PokerAttack.Shared.Enums;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace ChefKnifeStudios.PokerAttack.Server.BL.Services;

public interface IGameService
{
    Task StartGameAsync(string gameId, CancellationToken ct = default);
    Task StartRoundAsync(string gameId, CancellationToken ct = default);
    Task StartPlayerRunAsync(string playerId, int runTimeInSeconds, CancellationToken ct = default);
    Task PlayHandAsync(string playerId, List<CardDTO> hand, CancellationToken ct = default);
    Task DiscardAsync(string playerId, List<CardDTO> discardCards, CancellationToken ct = default);
    Task<int> GetPlayerScoreAsync(string playerId, CancellationToken ct = default);
    Task EndRoundAsync(string gameId, CancellationToken ct = default);
    Task<RoundDTO> GetLatestRoundFromGame(string gameId, CancellationToken ct = default);
    Task EndGameAsync(string gameId, CancellationToken ct = default);
    Task LeaveGameAsync(string gameId, string playerId, CancellationToken ct = default);
}

public class GameService(
    ILogger<GameService> logger,
    IKeyValueRepository<ActiveGame> activeGameRepository,
    IKeyValueRepository<GamePlayer> gamePlayerRepository,
    IKeyValueRepository<GameStates> gameStateRepository,
    IKeyValueRepository<Lobby> lobbyRepository,
    IRepository<Game> gameRepository,
    IRepository<Round> roundRepository,
    IPokerAttackNotificationHelper notificationHelper,
    IServiceScopeFactory serviceScopeFactory,
    IGameStateMachineService gameStateMachineService) : IGameService
{
    const int NumCardsInHand = 8;

    public async Task StartGameAsync(string gameId, CancellationToken ct = default)
    {
        //await gameStateMachineService.TransitionAsync(gameId, GameEvents.Next, ct);
    }

    public async Task StartRoundAsync(string gameId, CancellationToken ct = default)
    {
        const int _RUN_TIME_IN_SECONDS = 10;

        var game = await activeGameRepository.GetAsync(gameId)
            ?? throw new KeyNotFoundException($"Game not found. Game Id {gameId}");

        List<Task> taskList = [];
        int i = 0;
        foreach (var player in game.Players)
        {
            taskList.Add(StartRun(player.Id, _RUN_TIME_IN_SECONDS));
        }
        await Task.WhenAll(taskList);

        _ = StartServerTimerToEndRound(gameId, _RUN_TIME_IN_SECONDS);
    }

    // Start a run (per-player deck)
    async Task StartRun(string playerId, int runTimeInSeconds) =>
        await StartPlayerRunAsync(playerId, runTimeInSeconds);

    async Task StartServerTimerToEndRound(string gameId, int runTime)
    {
        await Task.Delay(runTime * 1000);

        using var scope = serviceScopeFactory.CreateScope();
        var scopedGameService = scope.ServiceProvider.GetRequiredService<IGameService>();
        var scopedGameStateMachineService = scope.ServiceProvider.GetRequiredService<IGameStateMachineService>();

        try
        {
            await scopedGameService.EndRoundAsync(gameId);
            await scopedGameStateMachineService.TransitionAsync(gameId, GameEvents.Next);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "An error occurred");
        }
    }

    public async Task StartPlayerRunAsync(string playerId, int runTimeInSeconds, CancellationToken ct = default)
    {
        var gamePlayer = await gamePlayerRepository.GetAsync(playerId, ct)
            ?? throw new KeyNotFoundException("Game Player not found");

        var deck = new Deck();
        deck.RandomizeDeck();

        gamePlayer.Deck = deck;
        gamePlayer.CardsInHand.Clear();
        gamePlayer.Score = 0;
        gamePlayer.PowerPoints = 0;
        await gamePlayerRepository.UpdateAsync(playerId, gamePlayer, ct);
        await ReplenishHandAsync(playerId, ct);

        var resBody = new RunStartedDTO()
        {
            RunTimeInSeconds = runTimeInSeconds,
            Cards = gamePlayer.CardsInHand.Select(x => x.MapToDTO()),
        };

        await notificationHelper.SendToPlayerAsync(playerId, new PokerAttackNotification
        (
            PokerAttackNotificationType.RunStarted,
            JsonSerializer.Serialize(resBody, JsonOptions.Get())
        ));
    }

    public async Task PlayHandAsync(string playerId, List<CardDTO> handDTO, CancellationToken ct = default)
    {
        var hand = handDTO.Select(x => x.MapToModel()).ToList();

        var gamePlayer = await gamePlayerRepository.GetAsync(playerId, ct)
            ?? throw new KeyNotFoundException("Game Player not found");

        // ✅ Remove matching cards from gamePlayer.CardsInHand (handles duplicates)
        foreach (var card in hand)
        {
            var match = gamePlayer.CardsInHand
                .FirstOrDefault(c => c.Rank == card.Rank && c.Suit == card.Suit);

            if (match == null)
            {
                throw new InvalidOperationException($"Player does not have card: {card.Rank} of {card.Suit}");
            }

            gamePlayer.CardsInHand.Remove(match);
        }

        // Now evaluate this played hand
        var result = HandEvaluator.EvaluateHand(hand);

        // Add score
        gamePlayer.Score += result.HandScore;
        await gamePlayerRepository.UpdateAsync(playerId, gamePlayer, ct);

        await ReplenishHandAsync(playerId, ct);

        // Send notification
        await notificationHelper.SendToPlayerAsync(playerId, new PokerAttackNotification
        (
            PokerAttackNotificationType.HandPlayed,
            JsonSerializer.Serialize(result.MapToDTO(gamePlayer.Score), JsonOptions.Get())
        ));
    }

    public async Task DiscardAsync(string playerId, List<CardDTO> discardCardsDTO, CancellationToken ct = default)
    {
        var discardCards = discardCardsDTO.Select(x => x.MapToModel()).ToList();

        var gamePlayer = await gamePlayerRepository.GetAsync(playerId, ct)
            ?? throw new KeyNotFoundException("Game Player not found");

        // ✅ Remove matching cards from gamePlayer.CardsInHand (handles duplicates)
        foreach (var card in discardCards)
        {
            var match = gamePlayer.CardsInHand
                .FirstOrDefault(c => c.Rank == card.Rank && c.Suit == card.Suit);

            if (match == null)
            {
                throw new InvalidOperationException($"Player does not have card: {card.Rank} of {card.Suit}");
            }

            gamePlayer.CardsInHand.Remove(match);
        }

        await ReplenishHandAsync(playerId, ct);
    }

    public async Task<int> GetPlayerScoreAsync(string playerId, CancellationToken ct = default)
        => (await gamePlayerRepository.GetAsync(playerId, ct)
            ?? throw new KeyNotFoundException("Game Player not found")).Score;

    public async Task EndRoundAsync(string gameId, CancellationToken ct = default)
    {
        var activeGame = await activeGameRepository.GetAsync(gameId, ct)
            ?? throw new ApplicationException($"Active Game not found: Active Game Id {gameId}");
        var game = await gameRepository.FirstOrDefaultAsync(new GetGameByClientIdSpec(gameId), ct)
            ?? throw new ApplicationException($"Game Record not found: Game Record Id {gameId}"); ;

        List<RoundScore> roundScores = [];
        foreach (var activeGamePlayer in activeGame.Players)
        {
            string playerId = activeGamePlayer.Id;
            int score = (await gamePlayerRepository.GetAsync(playerId, ct))?.Score ?? 0;
            roundScores.Add(
                new RoundScore
                {
                    ClientUserId = playerId,
                    ClientUserDisplayName = activeGamePlayer.Name,
                    Score = score,
                }
            );

            // Clear temp player data
            var gamePlayer = await gamePlayerRepository.GetAsync(playerId, ct)
                ?? throw new KeyNotFoundException("Game Player not found");
            var deck = new Deck();
            deck.RandomizeDeck();
            gamePlayer.Deck = deck;
            gamePlayer.CardsInHand.Clear();
            gamePlayer.Score = 0;
            gamePlayer.PowerPoints = 0;
            await gamePlayerRepository.UpdateAsync(playerId, gamePlayer, ct);
            await ReplenishHandAsync(playerId, ct);
        }

        await roundRepository.AddAsync(
            new Round
            {
                GameId = game.Id,
                RoundScores = roundScores,
            },
            ct
        );

        await notificationHelper.BroadcastToGameAsync(
            gameId,
            new PokerAttackNotification(PokerAttackNotificationType.RoundEnded, string.Empty)
        );

        await gameStateMachineService.TransitionAsync(gameId, GameEvents.Next, ct);
        _ = Task.Run(async () =>
        {
            await Task.Delay(5000);

            using var scope = serviceScopeFactory.CreateScope();
            var scopedGameStateMachineService = scope.ServiceProvider.GetRequiredService<IGameStateMachineService>();
            await scopedGameStateMachineService.TransitionAsync(gameId, GameEvents.Next, ct);
        });
    }

    public async Task<RoundDTO> GetLatestRoundFromGame(string gameId, CancellationToken ct = default)
    {
        var game = await gameRepository.FirstOrDefaultAsync(new GetGameByClientIdSpec(gameId), ct)
            ?? throw new ApplicationException($"Game not found: Game Id {gameId}");
        var latestRound = await roundRepository.FirstOrDefaultAsync(new GetLatestRoundByGameIdSpec(game.Id), ct)
            ?? throw new ApplicationException($"Latest Round not found: Game Id {game.Id}");
        return latestRound.MapToDTO();
    }

    public async Task EndGameAsync(string gameId, CancellationToken ct = default)
    {
        var activeGame = await activeGameRepository.GetAsync(gameId, ct)
            ?? throw new ApplicationException($"Active Game not found: Game Id {gameId}");
        
        foreach (var gamePlayer in activeGame.Players) await gamePlayerRepository.DeleteAsync(gamePlayer.Id, ct);
        await activeGameRepository.DeleteAsync(gameId, ct);
        await gameStateRepository.DeleteAsync(gameId, ct);

        foreach (var player in activeGame.Players)
        {
            await notificationHelper.LeaveGameGroupForUserAsync(player.Id, gameId, ct);
        }

        // Reopen the lobby
        var lobby = (await lobbyRepository.GetAllAsync(ct))
            .FirstOrDefault(x => x.Value.GameId == gameId)
            .Value
            ?? throw new ApplicationException($"Lobby not found with Game Id: Game Id {gameId}");
        lobby.GameId = null;
        await lobbyRepository.UpdateAsync(lobby.Id, lobby, ct);

        await notificationHelper.BroadcastToAllAsync(
            new PokerAttackNotification(
                PokerAttackNotificationType.LobbiesChanged, 
                JsonSerializer.Serialize(
                    new LobbyEventArgs { Lobby = lobby.MapToDTO() },
                    JsonOptions.Get()
                )
            )
        );
    }

    public async Task LeaveGameAsync(string gameId, string playerId, CancellationToken ct = default)
    {
        try
        {
            var lobby = await activeGameRepository.GetAsync(gameId, ct)
                ?? throw new ApplicationException($"Active Game not found: Game Id {gameId}");

            lobby.Players.RemoveWhere(x => x.Id == playerId);
            await gamePlayerRepository.DeleteAsync(playerId, ct);
            await activeGameRepository.UpdateAsync(gameId, lobby, ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Game ended before loser could leave (expected race-condition at game end).");
        }
    }

    async Task ReplenishHandAsync(string playerId, CancellationToken ct = default)
    {
        var gamePlayer = await gamePlayerRepository.GetAsync(playerId, ct)
            ?? throw new KeyNotFoundException("Game Player not found");
        var deck = gamePlayer.Deck;
        int numCardsToAdd = NumCardsInHand - gamePlayer.CardsInHand.Count();
        for (int i = 0; i < numCardsToAdd; i++)
            gamePlayer.CardsInHand.Add(deck.PullCard());
        await gamePlayerRepository.UpdateAsync(playerId, gamePlayer, ct);

        await notificationHelper.SendToPlayerAsync(playerId, new PokerAttackNotification
        (
            PokerAttackNotificationType.CardsDealt,
            JsonSerializer.Serialize(gamePlayer.CardsInHand.Select(x => x.MapToDTO()), JsonOptions.Get())
        ));
    }
}
