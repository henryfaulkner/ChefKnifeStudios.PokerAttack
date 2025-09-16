using ChefKnifeStudios.PokerAttack.Server.Core.Interfaces;
using ChefKnifeStudios.PokerAttack.Server.Core.Models;
using ChefKnifeStudios.PokerAttack.Server.WebAPI;
using ChefKnifeStudios.PokerAttack.Shared.DTOs.Lobby;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;

namespace ChefKnifeStudios.PokerAttack.Server.Infrastructure;

public class InMemoryLobbyRepository : ILobbyRepository
{
    private readonly ConcurrentDictionary<string, Lobby> _lobbies
        = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Creates an empty lobby.
    /// </summary>
    public Task<LobbyDTO> CreateLobbyAsync(string hostPlayerId, CancellationToken cancellationToken = default)
    {
        var gameId = GenerateGameId();
        var lobby = new Lobby { HostPlayerId = hostPlayerId, PlayerIds = new() { hostPlayerId }, };
        _lobbies.TryAdd(gameId, lobby);
        return Task.FromResult(lobby.MapToDTO(gameId));
    }

    /// <summary>
    /// Checks if a lobby exists.
    /// </summary>
    public Task<bool> LobbyExistsAsync(string gameId, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(_lobbies.ContainsKey(gameId));
    }

    /// <summary>
    /// Adds a player to the lobby.
    /// </summary>
    public Task AddPlayerToLobbyAsync(string gameId, string playerId, CancellationToken cancellationToken = default)
    {
        _lobbies.TryGetValue(gameId, out Lobby? lobby);

        if (lobby is { })
        {
            lock (lobby.PlayerIds)
            {
                lobby.PlayerIds.Add(playerId);
            }
        }
        return Task.CompletedTask;
    }

    /// <summary>
    /// Removes a player from the lobby.
    /// </summary>
    public Task RemovePlayerFromLobbyAsync(string gameId, string playerId, CancellationToken cancellationToken = default)
    {
        if (_lobbies.TryGetValue(gameId, out var lobby))
        {
            lock (lobby.PlayerIds)
            {
                lobby.PlayerIds.Remove(playerId);
            }
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Checks if a player is in the lobby.
    /// </summary>
    public Task<bool> IsPlayerInLobbyAsync(string gameId, string playerId, CancellationToken cancellationToken = default)
    {
        if (_lobbies.TryGetValue(gameId, out var lobby))
        {
            lock (lobby.PlayerIds)
            {
                return Task.FromResult(lobby.PlayerIds.Contains(playerId));
            }
        }

        return Task.FromResult(false);
    }

    /// <summary>
    /// Gets all players in the lobby.
    /// </summary>
    public Task<IEnumerable<string>> GetPlayersAsync(string gameId, CancellationToken cancellationToken = default)
    {
        if (_lobbies.TryGetValue(gameId, out var lobby))
        {
            lock (lobby.PlayerIds)
            {
                return Task.FromResult(lobby.PlayerIds.ToList().AsEnumerable());
            }
        }

        return Task.FromResult(Enumerable.Empty<string>());
    }

    /// <summary>
    /// Remove lobby, return all players who were in it.
    /// </summary>
    public Task<IEnumerable<string>> ShutDownLobbyAsync(string gameId, CancellationToken cancellationToken = default)
    {
        if (_lobbies.TryRemove(gameId, out var lobby))
        {
            IEnumerable<string>? result;
            lock (lobby.PlayerIds)
            {
                return Task.FromResult(lobby.PlayerIds.ToList().AsEnumerable());
            }
        }

        return Task.FromResult(Enumerable.Empty<string>());

    }

    public Task<IEnumerable<LobbyDTO>> GetLobbiesAsync(CancellationToken cancellationToken = default)
    {
        var result = new List<LobbyDTO>();
        foreach (var kvp in _lobbies)
        {
            result.Add(kvp.Value.MapToDTO(kvp.Key));
        }
        return Task.FromResult(result.AsEnumerable());
    }

    /// <summary>
    /// Optionally get the full lobby object (metadata + players).
    /// </summary>
    public Task<LobbyDTO?> GetLobbyAsync(string gameId, CancellationToken cancellationToken = default)
    {
        _lobbies.TryGetValue(gameId, out var lobby);
        return Task.FromResult(lobby?.MapToDTO(gameId));
    }

    /// <summary>
    /// Generates a random 6-character alphanumeric GameId.
    /// </summary>
    static string GenerateGameId()
    {
        const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
        var data = new byte[6];
        using (var rng = RandomNumberGenerator.Create())
        {
            rng.GetBytes(data);
        }

        var result = new StringBuilder(6);
        foreach (var b in data)
        {
            result.Append(chars[b % chars.Length]);
        }

        return result.ToString();
    }
}

