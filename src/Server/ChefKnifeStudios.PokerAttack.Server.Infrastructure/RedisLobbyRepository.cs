using ChefKnifeStudios.PokerAttack.Server.Core.Interfaces;
using ChefKnifeStudios.PokerAttack.Server.Core.Models;
using ChefKnifeStudios.PokerAttack.Shared.DTOs.Lobby;
using StackExchange.Redis;

namespace ChefKnifeStudios.PokerAttack.Server.Infrastructure;

public class RedisLobbyRepository : ILobbyRepository
{
    private readonly IDatabase _db;

    public RedisLobbyRepository(IConnectionMultiplexer redis)
    {
        _db = redis.GetDatabase();
    }

    private string LobbyKey(string gameId) => $"lobby:{gameId}";

    /// <summary>
    /// Creates an empty lobby.
    /// </summary>
    public async Task<LobbyDTO> CreateLobbyAsync(string gameId, CancellationToken cancellationToken = default)
    {
        // Use a Redis Set to store players; just initialize empty set if not exists
        bool exists = await _db.KeyExistsAsync(LobbyKey(gameId));
        if (!exists)
        {
            await _db.KeyExpireAsync(LobbyKey(gameId), TimeSpan.FromHours(1)); // optional TTL
        }
        return new LobbyDTO { GameId = string.Empty };
    }

    /// <summary>
    /// Checks if a lobby exists.
    /// </summary>
    public Task<bool> LobbyExistsAsync(string gameId, CancellationToken cancellationToken = default)
    {
        return _db.KeyExistsAsync(LobbyKey(gameId));
    }

    /// <summary>
    /// Adds a player to the lobby.
    /// </summary>
    public Task AddPlayerToLobbyAsync(string gameId, string playerId, CancellationToken cancellationToken = default)
    {
        return _db.SetAddAsync(LobbyKey(gameId), playerId);
    }

    /// <summary>
    /// Removes a player from the lobby.
    /// </summary>
    public Task RemovePlayerFromLobbyAsync(string gameId, string playerId, CancellationToken cancellationToken = default)
    {
        return _db.SetRemoveAsync(LobbyKey(gameId), playerId);
    }

    /// <summary>
    /// Checks if a player is in the lobby.
    /// </summary>
    public Task<bool> IsPlayerInLobbyAsync(string gameId, string playerId, CancellationToken cancellationToken = default)
    {
        return _db.SetContainsAsync(LobbyKey(gameId), playerId);
    }

    /// <summary>
    /// Gets all players in the lobby.
    /// </summary>
    public async Task<IEnumerable<string>> GetPlayersAsync(string gameId, CancellationToken cancellationToken = default)
    {
        var players = await _db.SetMembersAsync(LobbyKey(gameId));
        return players.Select(p => p.ToString());
    }

    public Task<IEnumerable<string>> ShutDownLobbyAsync(string gameId, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(Enumerable.Empty<string>());
    }

    public Task<IEnumerable<LobbyDTO>> GetLobbiesAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(Enumerable.Empty<LobbyDTO>());
    }

    public Task<LobbyDTO?> GetLobbyAsync(string gameId, CancellationToken cancellationToken = default)
    {
        return Task.FromResult((LobbyDTO?)null);
    }
}
