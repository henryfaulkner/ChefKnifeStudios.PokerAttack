using ChefKnifeStudios.PokerAttack.Server.Core.Interfaces.Repos;
using ChefKnifeStudios.PokerAttack.Server.Core.Models;
using System.Collections.Concurrent;

namespace ChefKnifeStudios.PokerAttack.Server.Infrastructure.Repos;

public class InMemoryPlayerDeckRepository : IPlayerDeckRepository
{
    readonly ConcurrentDictionary<string, Deck> _decks = new();

    public Task AddDeckAsync(string playerId, Deck deck, CancellationToken ct = default)
    {
        _decks[playerId] = deck;
        return Task.CompletedTask;
    }

    public Task<Deck?> GetDeckAsync(string playerId, CancellationToken ct = default)
    {
        _decks.TryGetValue(playerId, out var deck);
        return Task.FromResult(deck);
    }

    public Task UpdateDeckAsync(string playerId, Deck deck, CancellationToken ct = default)
    {
        _decks[playerId] = deck;
        return Task.CompletedTask;
    }

    public Task DeleteDeckAsync(string playerId, CancellationToken ct = default)
    {
        _decks.TryRemove(playerId, out _);
        return Task.CompletedTask;
    }
}
