using Ardalis.Result;
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
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace ChefKnifeStudios.PokerAttack.Server.BL.Services;

public interface IGameService
{
    Task<Result> StartGameAsync(string gameId, CancellationToken ct = default);
    Task<Result> StartRoundAsync(string gameId, CancellationToken ct = default);
    Task<Result> StartPlayerRunAsync(string playerId, int runTimeInSeconds, CancellationToken ct = default);
    Task<Result> PlayHandAsync(string playerId, List<CardDTO> hand, CancellationToken ct = default);
    Task<Result> DiscardAsync(string playerId, List<CardDTO> discardCards, CancellationToken ct = default);
    Task<Result<int>> GetPlayerScoreAsync(string playerId, CancellationToken ct = default);
    Task<Result<int>> GetPlayerWalletAsync(string playerId, CancellationToken ct = default);
    Task<Result> EndRoundAsync(string gameId, CancellationToken ct = default);
    Task<Result<RoundDTO>> GetLatestRoundFromGame(string gameId, CancellationToken ct = default);
    Task<Result> EndGameAsync(string gameId, CancellationToken ct = default);
    Task<Result> LeaveGameAsync(string gameId, string playerId, CancellationToken ct = default);
    Task<Result> StartEliminationAsync(string gameId, CancellationToken ct = default);
    Task<Result> FinishEliminationAsync(string gameId, CancellationToken ct = default);
    Task<Result> StartShoppingAsync(string gameId, CancellationToken ct = default);
    Task<Result> FinishShoppingAsync(string gameId, CancellationToken ct = default);
    Task<Result> EliminateDisconnectedPlayerAsync(string playerId, string gameId, CancellationToken ct = default);
}

public class GameService(
    ILogger<GameService> logger,
    IKeyValueRepository<ActiveGame> activeGameRepository,
    IKeyValueRepository<GamePlayer> gamePlayerRepository,
    IKeyValueRepository<GameStates?> gameStateRepository,
    IKeyValueRepository<Lobby> lobbyRepository,
    IRepository<Game> gameRepository,
    IRepository<Round> roundRepository,
    IPokerAttackNotificationHelper notificationHelper,
    IGameStateMachineService gameStateMachineService,
    IScoringRulesService scoringRulesService,
    IItemEffectsService itemEffectsService,
    IWagerService wagerService,
    IPlayerDisconnectionTracker disconnectionTracker) : IGameService
{
    const int _NUM_CARDS_IN_HAND = 8;
    const int _NUM_ROUNDS_BEFORE_ELIMINATION = 3;
    const int _BASE_HANDS_AVAILABLE = 5;
    const int _BASE_DISCARDS_AVAILABLE = 5;

    public async Task<Result> StartGameAsync(string gameId, CancellationToken ct = default)
    {
        return Result.Success();
    }

    public async Task<Result> StartRoundAsync(string gameId, CancellationToken ct = default)
    {
        var gameResult = await activeGameRepository.GetAsync(gameId, ct);
        if (!gameResult.IsSuccess || gameResult.Value is null)
            return Result.NotFound($"Game not found. Game Id {gameId}");

        var game = gameResult.Value;
        game.RoundNumber += 1;

        var updateResult = await activeGameRepository.UpdateAsync(gameId, game, ct);
        if (!updateResult.IsSuccess)
            return Result.Error("Failed to update game round number.");

        logger.LogInformation(
            "Round started: GameId={GameId}, RoundNumber={RoundNumber}, PlayerCount={PlayerCount}",
            gameId, game.RoundNumber, game.Players.Count);

        List<Task> taskList = [];
        foreach (var player in game.Players)
        {
            taskList.Add(StartRun(player.Id, Constants.RoundTimeMs));
        }
        await Task.WhenAll(taskList);

        await Task.Delay(Constants.RoundTimeMs);

        var endResult = await EndRoundAsync(gameId, ct);
        if (!endResult.IsSuccess)
            return endResult;

        return Result.Success();
    }

    public async Task<Result> StartPlayerRunAsync(string playerId, int runTimeInSeconds, CancellationToken ct = default)
    {
        var gamePlayerResult = await gamePlayerRepository.GetAsync(playerId, ct);
        if (!gamePlayerResult.IsSuccess || gamePlayerResult.Value is null)
            return Result.NotFound("Game Player not found");

        var gamePlayer = gamePlayerResult.Value;

        var deck = new Deck();
        deck.RandomizeDeck();

        var clearResult = await ClearGamePlayerDataAsync(playerId, gamePlayer, ct);
        if (!clearResult.IsSuccess)
            return clearResult;

        // Initialize hands and discards with base values + item buffs
        int handsBuff = itemEffectsService.GetHandsAvailableBuff(gamePlayer.ActiveItems);
        int discardsBuff = itemEffectsService.GetDiscardsAvailableBuff(gamePlayer.ActiveItems);
        gamePlayer.HandsRemaining = _BASE_HANDS_AVAILABLE + handsBuff;
        gamePlayer.DiscardsRemaining = _BASE_DISCARDS_AVAILABLE + discardsBuff;

        var updateResult = await gamePlayerRepository.UpdateAsync(playerId, gamePlayer, ct);
        if (!updateResult.IsSuccess)
            return Result.Error("Failed to update game player.");

        var replenishResult = await ReplenishHandAsync(playerId, ct);
        if (!replenishResult.IsSuccess)
            return replenishResult;

        // Reload to get updated cards
        var reloadResult = await gamePlayerRepository.GetAsync(playerId, ct);
        if (!reloadResult.IsSuccess || reloadResult.Value is null)
            return Result.NotFound("Game Player not found");

        gamePlayer = reloadResult.Value;

        var resBody = new RunStartedDTO()
        {
            Cards = gamePlayer.CardsInHand.Select(x => x.MapToDTO()),
            HandsAvailable = gamePlayer.HandsRemaining,
            DiscardsAvailable = gamePlayer.DiscardsRemaining,
        };

        await notificationHelper.SendToPlayerAsync(playerId, new PokerAttackNotification
        (
            PokerAttackNotificationType.RunStarted,
            JsonSerializer.Serialize(resBody, JsonOptions.Get())
        ));

        return Result.Success();
    }

    public async Task<Result> PlayHandAsync(string playerId, List<CardDTO> handDTO, CancellationToken ct = default)
    {
        var hand = handDTO.Select(x => x.MapToModel()).ToList();

        var gamePlayerResult = await gamePlayerRepository.GetAsync(playerId, ct);
        if (!gamePlayerResult.IsSuccess || gamePlayerResult.Value is null)
            return Result.NotFound("Game Player not found");

        var gamePlayer = gamePlayerResult.Value;

        // Check if player has hands remaining
        if (gamePlayer.HandsRemaining <= 0)
        {
            return Result.Invalid(new ValidationError("No hands remaining"));
        }

        // Remove matching cards from gamePlayer.CardsInHand (handles duplicates)
        foreach (var card in hand)
        {
            var match = gamePlayer.CardsInHand
                .FirstOrDefault(c => c.Rank == card.Rank && c.Suit == card.Suit);

            if (match == null)
            {
                return Result.Invalid(new ValidationError($"Player does not have card: {card.Rank} of {card.Suit}"));
            }

            gamePlayer.CardsInHand.Remove(match);
        }

        // Now evaluate this played hand
        var baseResultEval = new HandEvaluator(scoringRulesService).EvaluateHand(hand);
        if (!baseResultEval.IsSuccess)
            return Result.Error("Failed to evaluate hand.");

        var baseResult = baseResultEval.Value;

        // Apply item effects to the hand result
        var result = itemEffectsService.ApplyItemEffects(baseResult, hand, gamePlayer.ActiveItems);

        // Check wagers
        var completedWagers = wagerService.CheckWagers(gamePlayer.ActiveWagers, result, hand);
        int wagerChips = 0;
        foreach (var wagerResult in completedWagers)
        {
            wagerChips += wagerResult.ChipsAwarded;
            wagerResult.Wager.IsCompleted = true;

            // Notify player of wager completion
            await notificationHelper.SendToPlayerAsync(playerId, new PokerAttackNotification(
                PokerAttackNotificationType.WagerCompleted,
                JsonSerializer.Serialize(new WagerCompletedDTO
                {
                    WagerId = wagerResult.Wager.Id,
                    WagerName = wagerResult.Wager.Name,
                    WagerDescription = wagerResult.Wager.Description,
                    ChipsAwarded = wagerResult.ChipsAwarded
                }, JsonOptions.Get())
            ));
        }

        // Add score, wager chips, and decrement hands remaining
        gamePlayer.Score += result.HandScore;
        gamePlayer.Wallet += result.HandScore + wagerChips;
        gamePlayer.HandsRemaining--;

        var updateResult = await gamePlayerRepository.UpdateAsync(playerId, gamePlayer, ct);
        if (!updateResult.IsSuccess)
            return Result.Error("Failed to update game player.");

        var replenishResult = await ReplenishHandAsync(playerId, ct);
        if (!replenishResult.IsSuccess)
            return replenishResult;

        // Send notification
        await notificationHelper.SendToPlayerAsync(playerId, new PokerAttackNotification
        (
            PokerAttackNotificationType.HandPlayed,
            JsonSerializer.Serialize(result.MapToDTO(gamePlayer.Score), JsonOptions.Get())
        ));

        return Result.Success();
    }

    public async Task<Result> DiscardAsync(string playerId, List<CardDTO> discardCardsDTO, CancellationToken ct = default)
    {
        var discardCards = discardCardsDTO.Select(x => x.MapToModel()).ToList();

        var gamePlayerResult = await gamePlayerRepository.GetAsync(playerId, ct);
        if (!gamePlayerResult.IsSuccess || gamePlayerResult.Value is null)
            return Result.NotFound("Game Player not found");

        var gamePlayer = gamePlayerResult.Value;

        // Check if player has discards remaining
        if (gamePlayer.DiscardsRemaining <= 0)
        {
            return Result.Invalid(new ValidationError("No discards remaining"));
        }

        // ✅ Remove matching cards from gamePlayer.CardsInHand (handles duplicates)
        foreach (var card in discardCards)
        {
            var match = gamePlayer.CardsInHand
                .FirstOrDefault(c => c.Rank == card.Rank && c.Suit == card.Suit);

            if (match == null)
            {
                return Result.Invalid(new ValidationError($"Player does not have card: {card.Rank} of {card.Suit}"));
            }

            gamePlayer.CardsInHand.Remove(match);
        }

        // Decrement discards remaining
        gamePlayer.DiscardsRemaining--;

        var updateResult = await gamePlayerRepository.UpdateAsync(playerId, gamePlayer, ct);
        if (!updateResult.IsSuccess)
            return Result.Error("Failed to update game player.");

        var replenishResult = await ReplenishHandAsync(playerId, ct);
        if (!replenishResult.IsSuccess)
            return replenishResult;

        return Result.Success();
    }

    public async Task<Result<int>> GetPlayerScoreAsync(string playerId, CancellationToken ct = default)
    {
        var gamePlayerResult = await gamePlayerRepository.GetAsync(playerId, ct);
        if (!gamePlayerResult.IsSuccess || gamePlayerResult.Value is null)
            return Result.NotFound("Game Player not found");

        return Result.Success(gamePlayerResult.Value.Score);
    }

    public async Task<Result<int>> GetPlayerWalletAsync(string playerId, CancellationToken ct = default)
    {
        var gamePlayerResult = await gamePlayerRepository.GetAsync(playerId, ct);
        if (!gamePlayerResult.IsSuccess || gamePlayerResult.Value is null)
            return Result.NotFound("Game Player not found");

        return Result.Success(gamePlayerResult.Value.Wallet);
    }

    public async Task<Result> EndRoundAsync(string gameId, CancellationToken ct = default)
    {
        var activeGameResult = await activeGameRepository.GetAsync(gameId, ct);
        if (!activeGameResult.IsSuccess || activeGameResult.Value is null)
            return Result.NotFound($"Active Game not found: Active Game Id {gameId}");

        var activeGame = activeGameResult.Value;

        var game = await gameRepository.FirstOrDefaultAsync(new GetGameByClientIdSpec(gameId), ct);
        if (game is null)
            return Result.NotFound($"Game Record not found: Game Record Id {gameId}");

        List<RoundScore> roundScores = [];
        foreach (var activeGamePlayer in activeGame.Players)
        {
            string playerId = activeGamePlayer.Id;
            var playerResult = await gamePlayerRepository.GetAsync(playerId, ct);
            int score = playerResult.IsSuccess && playerResult.Value is not null ? playerResult.Value.Score : 0;
            roundScores.Add(
                new RoundScore
                {
                    ClientUserId = playerId,
                    ClientUserDisplayName = activeGamePlayer.Name,
                    Score = score,
                }
            );
        }

        await roundRepository.AddAsync(
            new Round
            {
                GameId = game.Id,
                RoundScores = roundScores,
            },
            ct
        );

        logger.LogInformation(
            "Round ended: GameId={GameId}, RoundNumber={RoundNumber}, PlayerCount={PlayerCount}",
            gameId, activeGame.RoundNumber, activeGame.Players.Count);

        await notificationHelper.BroadcastToGameAsync(
            gameId,
            new PokerAttackNotification(PokerAttackNotificationType.RoundEnded, string.Empty)
        );

        // Transition to scoreboard, then to elimination
        var transitionResult = await gameStateMachineService.TransitionAsync(gameId, GameEvents.Next, ct);
        if (!transitionResult.IsSuccess)
            return transitionResult;

        await Task.Delay(5000);

        var gameRounds = await roundRepository.ListAsync(new GetRoundsByGameIdSpec(game.Id), ct);
        int numRounds = gameRounds.Count;
        switch (numRounds)
        {
            case < _NUM_ROUNDS_BEFORE_ELIMINATION:
                var nextResult = await gameStateMachineService.TransitionAsync(gameId, GameEvents.Next, ct);
                if (!nextResult.IsSuccess)
                    return nextResult;
                break;
            case >= _NUM_ROUNDS_BEFORE_ELIMINATION:
                var eliminateResult = await gameStateMachineService.TransitionAsync(gameId, GameEvents.Eliminate, ct);
                if (!eliminateResult.IsSuccess)
                    return eliminateResult;
                break;
        }

        return Result.Success();
    }

    public async Task<Result<RoundDTO>> GetLatestRoundFromGame(string gameId, CancellationToken ct = default)
    {
        var game = await gameRepository.FirstOrDefaultAsync(new GetGameByClientIdSpec(gameId), ct);
        if (game is null)
            return Result.NotFound($"Game not found: Game Id {gameId}");

        var latestRound = await roundRepository.FirstOrDefaultAsync(new GetLatestRoundByGameIdSpec(game.Id), ct);
        if (latestRound is null)
            return Result.NotFound($"Latest Round not found: Game Id {game.Id}");

        var dtoResult = latestRound.MapToDTO();
        if (!dtoResult.IsSuccess)
            return Result.Error("Failed to map round to DTO.");

        return Result.Success(dtoResult.Value);
    }

    public async Task<Result> EndGameAsync(string gameId, CancellationToken ct = default)
    {
        var activeGameResult = await activeGameRepository.GetAsync(gameId, ct);
        if (!activeGameResult.IsSuccess || activeGameResult.Value is null)
            return Result.NotFound($"Active Game not found: Game Id {gameId}");

        var activeGame = activeGameResult.Value;

        // Cancel all disconnection timers for players in this game
        foreach (var player in activeGame.Players)
        {
            disconnectionTracker.CancelDisconnectionTimer(player.Id);
        }

        foreach (var gamePlayer in activeGame.Players)
        {
            var deleteResult = await gamePlayerRepository.DeleteAsync(gamePlayer.Id, ct);
            if (!deleteResult.IsSuccess)
                logger.LogWarning("Failed to delete game player {PlayerId}", gamePlayer.Id);
        }

        var deleteActiveResult = await activeGameRepository.DeleteAsync(gameId, ct);
        if (!deleteActiveResult.IsSuccess)
            return Result.Error("Failed to delete active game.");

        var deleteStateResult = await gameStateRepository.DeleteAsync(gameId, ct);
        if (!deleteStateResult.IsSuccess)
            return Result.Error("Failed to delete game state.");

        logger.LogInformation(
            "Game ended: GameId={GameId}, PlayerCount={PlayerCount}",
            gameId, activeGame.Players.Count);

        foreach (var player in activeGame.Players)
        {
            await notificationHelper.LeaveGameGroupForUserAsync(player.Id, gameId, ct);
        }

        // Reopen the lobby
        var lobbiesResult = await lobbyRepository.GetAllAsync(ct);
        if (!lobbiesResult.IsSuccess)
            return Result.Error("Failed to retrieve lobbies.");

        var lobbyKvp = lobbiesResult.Value.FirstOrDefault(x => x.Value.GameId == gameId);
        if (lobbyKvp.Value is null)
            return Result.NotFound($"Lobby not found with Game Id: Game Id {gameId}");

        var lobby = lobbyKvp.Value;
        lobby.GameId = null;

        var updateLobbyResult = await lobbyRepository.UpdateAsync(lobby.Id, lobby, ct);
        if (!updateLobbyResult.IsSuccess)
            return Result.Error("Failed to update lobby.");

        await notificationHelper.BroadcastToAllAsync(
            new PokerAttackNotification(
                PokerAttackNotificationType.LobbiesChanged,
                JsonSerializer.Serialize(
                    new LobbyEventArgs { Lobby = lobby.MapToDTO() },
                    JsonOptions.Get()
                )
            )
        );

        return Result.Success();
    }

    public async Task<Result> LeaveGameAsync(string gameId, string playerId, CancellationToken ct = default)
    {
        try
        {
            await notificationHelper.LeaveGameGroupForUserAsync(playerId, gameId, ct);

            var activeGameResult = await activeGameRepository.GetAsync(gameId, ct);
            if (!activeGameResult.IsSuccess || activeGameResult.Value is null)
            {
                logger.LogWarning("Cannot leave game: Game {gameId} not found for player {playerId}", gameId, playerId);
                return Result.Success(); // Game already ended
            }

            var activeGame = activeGameResult.Value;
            activeGame.Players.RemoveWhere(x => x.Id == playerId);

            var updateResult = await activeGameRepository.UpdateAsync(gameId, activeGame, ct);
            if (!updateResult.IsSuccess)
                logger.LogWarning("Failed to update game {gameId} when removing player {playerId}", gameId, playerId);

            var deleteResult = await gamePlayerRepository.DeleteAsync(playerId, ct);
            if (!deleteResult.IsSuccess)
                logger.LogWarning("Failed to delete game player {playerId}", playerId);

            return Result.Success();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Game ended before loser could leave (expected race-condition at game end).");
            return Result.Success(); // Not a failure - expected race condition
        }
    }

    // Start a run (per-player deck)
    async Task StartRun(string playerId, int runTimeInSeconds) =>
        await StartPlayerRunAsync(playerId, runTimeInSeconds);

    async Task<Result> ReplenishHandAsync(string playerId, CancellationToken ct = default)
    {
        var gamePlayerResult = await gamePlayerRepository.GetAsync(playerId, ct);
        if (!gamePlayerResult.IsSuccess || gamePlayerResult.Value is null)
            return Result.NotFound("Game Player not found");

        var gamePlayer = gamePlayerResult.Value;
        var deck = gamePlayer.Deck;
        int numCardsToAdd = _NUM_CARDS_IN_HAND - gamePlayer.CardsInHand.Count();
        for (int i = 0; i < numCardsToAdd; i++)
            gamePlayer.CardsInHand.Add(deck.PullCard());

        var updateResult = await gamePlayerRepository.UpdateAsync(playerId, gamePlayer, ct);
        if (!updateResult.IsSuccess)
            return Result.Error("Failed to update game player.");

        await notificationHelper.SendToPlayerAsync(playerId, new PokerAttackNotification
        (
            PokerAttackNotificationType.CardsDealt,
            JsonSerializer.Serialize(gamePlayer.CardsInHand.Select(x => x.MapToDTO()), JsonOptions.Get())
        ));

        return Result.Success();
    }

    public async Task<Result> StartEliminationAsync(string gameId, CancellationToken ct = default)
    {
        var activeGameResult = await activeGameRepository.GetAsync(gameId, ct);
        if (!activeGameResult.IsSuccess || activeGameResult.Value is null)
            return Result.NotFound($"Active Game not found: {gameId}");

        var activeGame = activeGameResult.Value;

        // Collect non-eliminated players and their backend state
        var playerStates = new List<(Player player, GamePlayer state)>();
        foreach (var player in activeGame.Players)
        {
            var stateResult = await gamePlayerRepository.GetAsync(player.Id, ct);
            if (stateResult.IsSuccess && stateResult.Value is not null && !stateResult.Value.IsEliminated)
                playerStates.Add((player, stateResult.Value));
        }

        // Need at least two players to eliminate someone
        if (playerStates.Count <= 1)
            return Result.Success();

        // If all remaining players are tied, do nothing
        var distinctScores = playerStates.Select(p => p.state.Score).Distinct().Count();
        if (distinctScores == 1)
            return Result.Success();

        // Find lowest score and eliminate all players who have it
        int minScore = playerStates.Min(p => p.state.Score);
        var toEliminate = playerStates.Where(p => p.state.Score == minScore).ToList();

        foreach (var (player, state) in toEliminate)
        {
            state.IsEliminating = true;
            var updateResult = await gamePlayerRepository.UpdateAsync(player.Id, state, ct);
            if (!updateResult.IsSuccess)
                logger.LogWarning("Failed to mark player {PlayerId} as eliminating", player.Id);
        }

        // Notify clients with the list of eliminating players
        var eventArgs = new EliminationStartedArgs(
            toEliminate.Select(p => new EliminatingPlayerDTO(p.player.Id, p.player.Name)).ToList()
        );
        var payload = JsonSerializer.Serialize(eventArgs, JsonOptions.Get());
        await notificationHelper.BroadcastToGameAsync(
            gameId,
            new PokerAttackNotification(PokerAttackNotificationType.EliminationStarted, payload),
            ct
        );

        return Result.Success();
    }

    public async Task<Result> FinishEliminationAsync(string gameId, CancellationToken ct = default)
    {
        var activeGameResult = await activeGameRepository.GetAsync(gameId, ct);
        if (!activeGameResult.IsSuccess || activeGameResult.Value is null)
            return Result.NotFound($"Active Game not found: {gameId}");

        var activeGame = activeGameResult.Value;
        var eliminatedPlayers = new List<(string Id, string Name)>();

        // Finalize elimination flags and persist
        foreach (var player in activeGame.Players)
        {
            var gamePlayerResult = await gamePlayerRepository.GetAsync(player.Id, ct);
            if (!gamePlayerResult.IsSuccess || gamePlayerResult.Value is null) continue;

            var gamePlayer = gamePlayerResult.Value;

            if (gamePlayer.IsEliminating)
            {
                gamePlayer.IsEliminated = true;
                eliminatedPlayers.Add((player.Id, player.Name));
            }

            // Clear transient flag
            gamePlayer.IsEliminating = false;

            var updateResult = await gamePlayerRepository.UpdateAsync(player.Id, gamePlayer, ct);
            if (!updateResult.IsSuccess)
                logger.LogWarning("Failed to update player {PlayerId} elimination status", player.Id);
        }

        // Build losers list
        var losers = eliminatedPlayers
            .Select(p => new EliminatingPlayerDTO(p.Id, p.Name))
            .ToList();

        logger.LogInformation(
            "Players eliminated: GameId={GameId}, EliminatedCount={EliminatedCount}, EliminatedPlayers={EliminatedPlayers}",
            gameId, eliminatedPlayers.Count, string.Join(", ", eliminatedPlayers.Select(p => p.Name)));

        // Determine remaining players after elimination
        var remaining = new List<(string Id, string Name)>();
        foreach (var player in activeGame.Players)
        {
            var stateResult = await gamePlayerRepository.GetAsync(player.Id, ct);
            if (stateResult.IsSuccess && stateResult.Value is not null && !stateResult.Value.IsEliminated)
            {
                remaining.Add((player.Id, player.Name));
            }
        }

        // Use a plain object for payload so we don't get anonymous-type re-assignment errors.
        EliminationFinishedArgs eventArgs;
        (string Id, string Name)? winner = null;
        if (remaining.Count == 1)
        {
            winner = remaining[0];
            eventArgs = new EliminationFinishedArgs(losers, new EliminatingPlayerDTO(winner.Value.Id, winner.Value.Name));
            var endResult = await EndGameAsync(gameId, ct);
            if (!endResult.IsSuccess)
                return endResult;
        }
        else
        {
            eventArgs = new EliminationFinishedArgs(losers, null);
        }

        await notificationHelper.SendToPlayersAsync(
            gameId,
            losers.Select(x => x.PlayerId),
            new PokerAttackNotification(PokerAttackNotificationType.GameLost, null),
            ct
        );

        foreach (var loser in losers)
        {
            var leaveResult = await LeaveGameAsync(gameId, loser.PlayerId, ct);
            if (!leaveResult.IsSuccess)
                logger.LogWarning("Failed to remove loser {PlayerId} from game {GameId}", loser.PlayerId, gameId);
        }

        if (winner is { Id: string winnerId })
        {
            await notificationHelper.SendToPlayerAsync(
                winnerId,
                new PokerAttackNotification(PokerAttackNotificationType.GameWon, null),
                ct
            );
        }

        var payload = JsonSerializer.Serialize(eventArgs, JsonOptions.Get());
        await notificationHelper.BroadcastToGameAsync(
            gameId,
            new PokerAttackNotification(PokerAttackNotificationType.EliminationFinished, payload),
            ct
        );

        var transitionResult = await gameStateMachineService.TransitionAsync(gameId, GameEvents.Next, ct);
        if (!transitionResult.IsSuccess)
            return transitionResult;

        return Result.Success();
    }

    public async Task<Result> StartShoppingAsync(string gameId, CancellationToken ct = default)
    {
        return Result.Success();
    }

    public async Task<Result> FinishShoppingAsync(string gameId, CancellationToken ct = default)
    {
        var transitionResult = await gameStateMachineService.TransitionAsync(gameId, GameEvents.Next, ct);
        if (!transitionResult.IsSuccess)
            return transitionResult;

        return Result.Success();
    }

    public async Task<Result> EliminateDisconnectedPlayerAsync(string playerId, string gameId, CancellationToken ct = default)
    {
        try
        {
            var activeGameResult = await activeGameRepository.GetAsync(gameId, ct);
            if (!activeGameResult.IsSuccess || activeGameResult.Value is null)
            {
                logger.LogWarning("Cannot eliminate disconnected player {playerId}: Game {gameId} not found", playerId, gameId);
                return Result.Success();
            }

            var activeGame = activeGameResult.Value;

            var gameStateResult = await gameStateRepository.GetAsync(gameId, ct);
            if (!gameStateResult.IsSuccess || gameStateResult.Value is null)
            {
                logger.LogInformation("Cannot eliminate disconnected player {playerId}: Game {gameId} state cleanup already happened", playerId, gameId);
                return Result.Success();
            }

            var gamePlayerResult = await gamePlayerRepository.GetAsync(playerId, ct);
            if (!gamePlayerResult.IsSuccess || gamePlayerResult.Value is null)
            {
                logger.LogWarning("Cannot eliminate disconnected player {playerId}: Player not found in game", playerId);
                return Result.Success();
            }

            var gamePlayer = gamePlayerResult.Value;

            // Check if player is already eliminated
            if (gamePlayer.IsEliminated)
            {
                logger.LogInformation("Player {playerId} is already eliminated", playerId);
                return Result.Success();
            }

            // Find player info
            var player = activeGame.Players.FirstOrDefault(p => p.Id == playerId);
            if (player == null)
            {
                logger.LogWarning("Player {playerId} not found in active game {gameId}", playerId, gameId);
                return Result.Success();
            }

            // Mark player as eliminated
            gamePlayer.IsEliminated = true;
            var updateResult = await gamePlayerRepository.UpdateAsync(playerId, gamePlayer, ct);
            if (!updateResult.IsSuccess)
                logger.LogWarning("Failed to update player {playerId} elimination status", playerId);

            logger.LogInformation("Player {playerId} ({playerName}) eliminated from game {gameId} due to disconnection timeout",
                playerId, player.Name, gameId);

            // Notify all players in the game
            var notification = new PokerAttackNotification(
                PokerAttackNotificationType.PlayerDisconnected,
                JsonSerializer.Serialize(new
                {
                    PlayerId = playerId,
                    PlayerName = player.Name,
                    Reason = "Connection timeout - player eliminated"
                }, JsonOptions.Get())
            );

            await notificationHelper.BroadcastToGameAsync(gameId, notification, ct);

            // Check if game should end (only one player remaining)
            int remainingPlayers = 0;
            foreach (var p in activeGame.Players)
            {
                var pStateResult = await gamePlayerRepository.GetAsync(p.Id, ct);
                if (pStateResult.IsSuccess && pStateResult.Value is not null && !pStateResult.Value.IsEliminated)
                    remainingPlayers++;
            }

            if (remainingPlayers <= 1)
            {
                logger.LogInformation("Only {count} player(s) remaining in game {gameId} after disconnection elimination. Ending game.",
                    remainingPlayers, gameId);
                var endResult = await EndGameAsync(gameId, ct);
                if (!endResult.IsSuccess)
                    logger.LogWarning("Failed to end game {gameId} after disconnection elimination", gameId);
            }

            return Result.Success();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error eliminating disconnected player {playerId} from game {gameId}", playerId, gameId);
            return Result.Success(); // Don't propagate errors - this is a background operation
        }
    }

    async Task<Result> ClearGamePlayerDataAsync(string playerId, GamePlayer gamePlayer, CancellationToken ct = default)
    {
        var deck = new Deck();
        deck.RandomizeDeck();
        gamePlayer.Deck = deck;
        gamePlayer.CardsInHand.Clear();
        gamePlayer.Score = 0;
        gamePlayer.PowerPoints = 0;

        var updateResult = await gamePlayerRepository.UpdateAsync(playerId, gamePlayer, ct);
        if (!updateResult.IsSuccess)
            return Result.Error("Failed to clear game player data.");

        return Result.Success();
    }
}
