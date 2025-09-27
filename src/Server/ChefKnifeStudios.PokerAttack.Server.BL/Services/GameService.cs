using ChefKnifeStudios.PokerAttack.Server.Core.Interfaces.Repos;
using ChefKnifeStudios.PokerAttack.Server.Core.Models;
using ChefKnifeStudios.PokerAttack.Shared.DTOs.Gameplay;
using ChefKnifeStudios.PokerAttack.Shared.Enums;

namespace ChefKnifeStudios.PokerAttack.Server.BL.Services;

public interface IGameService
{
    Task StartPlayerRunAsync(string playerId, CancellationToken ct = default);
    Task<List<Card>> DealHandAsync(string playerId, int count, CancellationToken ct = default);
    Task<HandResult> PlayHandAsync(string playerId, List<CardDTO> hand, CancellationToken ct = default);
    Task<int?> GetPlayerScoreAsync(string playerId, CancellationToken ct = default);
}

public class GameService : IGameService
{
    readonly IPlayerScoreRepository _scoreRepository;
    readonly IPlayerDeckRepository _deckRepository;

    public GameService(
        IPlayerScoreRepository scoreRepository,
        IPlayerDeckRepository deckRepository)
    {
        _scoreRepository = scoreRepository;
        _deckRepository = deckRepository;
    }

    public async Task StartPlayerRunAsync(string playerId, CancellationToken ct = default)
    {
        var deck = new Deck();
        deck.RandomizeDeck();
        await _deckRepository.AddDeckAsync(playerId, deck, ct);
        await _scoreRepository.AddAsync(playerId, 0, ct);
    }

    public async Task<List<Card>> DealHandAsync(string playerId, int count, CancellationToken ct = default)
    {
        var deck = await _deckRepository.GetDeckAsync(playerId, ct)
            ?? throw new KeyNotFoundException("Player deck not found");

        var hand = new List<Card>();
        for (int i = 0; i < count; i++)
            hand.Add(deck.PullCard());

        await _deckRepository.UpdateDeckAsync(playerId, deck, ct);
        return hand;
    }

    public async Task<HandResult> PlayHandAsync(string playerId, List<CardDTO> handDTO, CancellationToken ct = default)
    {
        var hand = handDTO.Select(x => x.MapToModel()).ToList();
        var result = HandEvaluator.EvaluateHand(hand);
        int totalScore = result.BaseChips * result.BaseMultiplier;

        var current = await _scoreRepository.GetAsync(playerId, ct) ?? 0;
        await _scoreRepository.UpdateAsync(playerId, current + totalScore, ct);

        return result;
    }

    public Task<int?> GetPlayerScoreAsync(string playerId, CancellationToken ct = default)
        => _scoreRepository.GetAsync(playerId, ct);
}
