using ChefKnifeStudios.PokerAttack.Server.Core.Interfaces.Repos;
using ChefKnifeStudios.PokerAttack.Server.Core.Models;
using System.Collections.Concurrent;

namespace ChefKnifeStudios.PokerAttack.Server.Infrastructure.Repos;

public class InMemoryGamePlayerRepository : IGamePlayerRepository
{
    private readonly ConcurrentDictionary<string, GamePlayer> _players = new();

    public Task AddAsync(string playerId, GamePlayer player, CancellationToken ct = default)
    {
        if (!_players.TryAdd(playerId, player))
            throw new InvalidOperationException($"Player {playerId} already exists.");

        return Task.CompletedTask;
    }

    public Task<GamePlayer?> GetAsync(string playerId, CancellationToken ct = default)
    {
        _players.TryGetValue(playerId, out var player);
        return Task.FromResult(player);
    }

    public Task UpdateAsync(string playerId, GamePlayer player, CancellationToken ct = default)
    {
        if (!_players.ContainsKey(playerId))
            throw new KeyNotFoundException($"Player {playerId} not found.");

        _players[playerId] = player;
        return Task.CompletedTask;
    }

    public Task DeleteAsync(string playerId, CancellationToken ct = default)
    {
        _players.TryRemove(playerId, out _);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyDictionary<string, GamePlayer>> GetAllAsync(CancellationToken ct = default)
    {
        return Task.FromResult((IReadOnlyDictionary<string, GamePlayer>)_players);
    }
}
