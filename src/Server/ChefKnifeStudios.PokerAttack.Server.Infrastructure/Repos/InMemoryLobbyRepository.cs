using ChefKnifeStudios.PokerAttack.Server.Core.Interfaces.Repos;
using ChefKnifeStudios.PokerAttack.Server.Core.Models;
using System.Collections.Concurrent;

namespace ChefKnifeStudios.PokerAttack.Server.Infrastructure.Repos;

public class InMemoryLobbyRepository : ILobbyRepository
{
    private readonly ConcurrentDictionary<string, Lobby> _lobbies
        = new(StringComparer.OrdinalIgnoreCase);

    public Task AddLobbyAsync(string gameId, Lobby lobby, CancellationToken cancellationToken = default)
    {
        _lobbies.TryAdd(gameId, lobby);
        return Task.CompletedTask;
    }

    public Task<bool> LobbyExistsAsync(string gameId, CancellationToken cancellationToken = default)
        => Task.FromResult(_lobbies.ContainsKey(gameId));

    public Task<Lobby?> GetLobbyAsync(string gameId, CancellationToken cancellationToken = default)
    {
        _lobbies.TryGetValue(gameId, out var lobby);
        return Task.FromResult(lobby);
    }

    public Task<IEnumerable<KeyValuePair<string, Lobby>>> GetAllLobbiesAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(_lobbies.Select(kvp =>
        {
            return kvp;
        }).AsEnumerable());

    public Task RemoveLobbyAsync(string gameId, CancellationToken cancellationToken = default)
    {
        _lobbies.TryRemove(gameId, out _);
        return Task.CompletedTask;
    }

    public Task UpdateLobbyAsync(string gameId, Lobby lobby, CancellationToken cancellationToken = default)
    {
        _lobbies[gameId] = lobby;
        return Task.CompletedTask;
    }
}
