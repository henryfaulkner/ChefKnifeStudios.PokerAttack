using ChefKnifeStudios.PokerAttack.Server.Core.Interfaces.Repos;
using ChefKnifeStudios.PokerAttack.Server.Core.Models;
using ChefKnifeStudios.PokerAttack.Server.Data.Models;
using ChefKnifeStudios.PokerAttack.Server.Data.Repos;
using ChefKnifeStudios.PokerAttack.Server.Data.Specifications;
using ChefKnifeStudios.PokerAttack.Shared.DTOs.Gameplay;
using ChefKnifeStudios.PokerAttack.Shared.Enums;

namespace ChefKnifeStudios.PokerAttack.Server.BL.Services;

public interface IGameService
{
    Task StartPlayerRunAsync(string playerId, CancellationToken ct = default);
    Task<List<Card>> DealHandAsync(string playerId, int count, CancellationToken ct = default);
    Task<HandResult> PlayHandAsync(string playerId, List<CardDTO> hand, CancellationToken ct = default);
    Task<int?> GetPlayerScoreAsync(string playerId, CancellationToken ct = default);
    Task EndRoundAsync(string lobbyId, CancellationToken ct = default);
    Task<RoundDTO> GetLatestRoundFromGame(string lobbyId, CancellationToken ct = default);
}

public class GameService(
    ILobbyRepository lobbyRepository,
    IPlayerScoreRepository scoreRepository,
    IPlayerDeckRepository deckRepository,
    IRepository<Game> gameRepository,
    IRepository<Round> roundRepository) : IGameService
{
    public async Task StartPlayerRunAsync(string playerId, CancellationToken ct = default)
    {
        var deck = new Deck();
        deck.RandomizeDeck();
        await deckRepository.AddDeckAsync(playerId, deck, ct);
        await scoreRepository.AddAsync(playerId, 0, ct);
    }

    public async Task<List<Card>> DealHandAsync(string playerId, int count, CancellationToken ct = default)
    {
        var deck = await deckRepository.GetDeckAsync(playerId, ct)
            ?? throw new KeyNotFoundException("Player deck not found");

        var hand = new List<Card>();
        for (int i = 0; i < count; i++)
            hand.Add(deck.PullCard());

        await deckRepository.UpdateDeckAsync(playerId, deck, ct);
        return hand;
    }

    public async Task<HandResult> PlayHandAsync(string playerId, List<CardDTO> handDTO, CancellationToken ct = default)
    {
        var hand = handDTO.Select(x => x.MapToModel()).ToList();
        var result = HandEvaluator.EvaluateHand(hand);
        int totalScore = result.BaseChips * result.BaseMultiplier;

        var current = await scoreRepository.GetAsync(playerId, ct) ?? 0;
        await scoreRepository.UpdateAsync(playerId, current + totalScore, ct);

        return result;
    }

    public Task<int?> GetPlayerScoreAsync(string playerId, CancellationToken ct = default)
        => scoreRepository.GetAsync(playerId, ct);

    public async Task EndRoundAsync(string lobbyId, CancellationToken ct = default)
    {
        var lobby = await lobbyRepository.GetLobbyAsync(lobbyId, ct)
            ?? throw new ApplicationException($"Lobby not found: Lobby Id {lobbyId}");
        var game = await gameRepository.FirstOrDefaultAsync(new GetGameByClientIdSpec(lobbyId), ct)
            ?? throw new ApplicationException($"Game not found: Lobby Id {lobbyId}"); ;

        List<RoundScore> roundScores = [];
        foreach (var lobbyPlayer in lobby.Players)
        {
            int score = await scoreRepository.GetAsync(lobbyPlayer.Id, ct) ?? 0;
            roundScores.Add(
                new RoundScore
                {
                    ClientUserId = lobbyPlayer.Id,
                    ClientUserDisplayName = lobbyPlayer.Name,
                    Score = score,
                }
            );

            // Clear temp player data
            await scoreRepository.DeleteAsync(lobbyPlayer.Id, ct);
            await deckRepository.DeleteDeckAsync(lobbyPlayer.Id, ct);
        }

        await roundRepository.AddAsync(
            new Round
            {
                GameId = game.Id,
                RoundScores = roundScores,
            },
            ct
        );
    }

    public async Task<RoundDTO> GetLatestRoundFromGame(string lobbyId, CancellationToken ct = default)
    {
        var game = await gameRepository.FirstOrDefaultAsync(new GetGameByClientIdSpec(lobbyId), ct)
            ?? throw new ApplicationException($"Game not found: Lobby Id {lobbyId}");
        var latestRound = await roundRepository.FirstOrDefaultAsync(new GetLatestRoundByGameIdSpec(game.Id), ct)
            ?? throw new ApplicationException($"Latest Round not found: Game Id {game.Id}");
        return latestRound.MapToDTO();
    }
}
