using ChefKnifeStudios.PokerAttack.Server.Core.Interfaces;
using ChefKnifeStudios.PokerAttack.Shared.DTOs.SignalR;
using Microsoft.AspNetCore.SignalR;

namespace ChefKnifeStudios.PokerAttack.Server.WebAPI.SignalR;

public class PokerAttackNotificationHelper : IPokerAttackNotificationHelper
{
    readonly IHubContext<SignalRNotificationHub, ISignalRNotificationClient> _hubContext;
    readonly IPlayerConnectionTracker _connectionTracker;
    readonly ILogger<PokerAttackNotificationHelper> _logger;

    // Cache group names (lobbyId → group)
    readonly Dictionary<string, string> _lobbyGroups = new();
    // Cache group names (gameId → group)
    readonly Dictionary<string, string> _gameGroups = new();

    public PokerAttackNotificationHelper(
        IHubContext<SignalRNotificationHub, ISignalRNotificationClient> hubContext,
        IPlayerConnectionTracker connectionTracker,
        ILogger<PokerAttackNotificationHelper> logger)
    {
        _hubContext = hubContext;
        _connectionTracker = connectionTracker;
        _logger = logger;
    }

    /// <summary>
    /// Send an update to a specific player
    /// </summary>
    public async Task SendToPlayerAsync(string playerId, PokerAttackNotification notification, CancellationToken cancellationToken = default)
    {
        try
        {
            await _hubContext.Clients.User(playerId).ReceivePokerAttackNotification(notification, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending notification to player {playerId}", playerId);
            throw;
        }
    }

    /// <summary>
    /// Broadcast update to all players in a lobby
    /// </summary>
    public async Task BroadcastToLobbyAsync(string lobbyId, PokerAttackNotification notification, CancellationToken cancellationToken = default)
    {
        try
        {
            var groupName = GetLobbyGroupName(lobbyId);
            var group = _hubContext.Clients.Group(groupName);
            await group.ReceivePokerAttackNotification(notification, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error broadcasting notification to lobby {lobbyId}", lobbyId);
            throw;
        }
    }

    /// <summary>
    /// Broadcast update to all players in a game
    /// </summary>
    public async Task BroadcastToGameAsync(string gameId, PokerAttackNotification notification, CancellationToken cancellationToken = default)
    {
        try
        {
            var groupName = GetGameGroupName(gameId);
            var group = _hubContext.Clients.Group(groupName);
            await group.ReceivePokerAttackNotification(notification, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error broadcasting notification to game {gameId}", gameId);
            throw;
        }
    }

    /// <summary>
    /// Send to a subset of players in a game
    /// </summary>
    public async Task SendToPlayersAsync(string gameId, IEnumerable<string> playerIds, PokerAttackNotification notification, CancellationToken cancellationToken = default)
    {
        try
        {
            var connections = playerIds.ToList();
            await _hubContext.Clients.Users(connections).ReceivePokerAttackNotification(notification, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending notification to players in game {gameId}", gameId);
            throw;
        }
    }

    /// <summary>
    /// Send a notification to all players
    /// </summary>
    public async Task BroadcastToAllAsync(PokerAttackNotification notification, CancellationToken cancellationToken = default)
    {
        try
        {
            await _hubContext.Clients.All.ReceivePokerAttackNotification(notification, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error broadcasting notification to all clients");
            throw;
        }
    }

    /// <summary>
    /// Get or generate the SignalR group name for a lobby
    /// </summary>
    public string GetLobbyGroupName(string lobbyId)
    {
        if (_lobbyGroups.TryGetValue(lobbyId, out var groupName))
            return groupName;

        groupName = $"lobby_{lobbyId}";
        _lobbyGroups[lobbyId] = groupName;

        return groupName;
    }

    /// <summary>
    /// Get or generate the SignalR group name for a game
    /// </summary>
    public string GetGameGroupName(string gameId)
    {
        if (_gameGroups.TryGetValue(gameId, out var groupName))
            return groupName;

        groupName = $"game_{gameId}";
        _gameGroups[gameId] = groupName;

        return groupName;
    }

    // --- convenience user-based methods that resolve connection ids using tracker ---

    public async Task JoinLobbyGroupForUserAsync(string userId, string lobbyId, CancellationToken cancellationToken = default)
    {
        try
        {
            var conns = _connectionTracker.GetConnections(userId);
            if (conns.Count == 0) return;
            var groupName = GetLobbyGroupName(lobbyId);

            foreach (var conn in conns)
                await _hubContext.Groups.AddToGroupAsync(conn, groupName, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error adding user {userId} to lobby group {lobbyId}", userId, lobbyId);
            throw;
        }
    }

    public async Task LeaveLobbyGroupForUserAsync(string userId, string lobbyId, CancellationToken cancellationToken = default)
    {
        try
        {
            var conns = _connectionTracker.GetConnections(userId);
            if (conns.Count == 0) return;
            var groupName = GetLobbyGroupName(lobbyId);

            foreach (var conn in conns)
                await _hubContext.Groups.RemoveFromGroupAsync(conn, groupName, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error removing user {userId} from lobby group {lobbyId}", userId, lobbyId);
            throw;
        }
    }

    public async Task JoinGameGroupForUserAsync(string userId, string gameId, CancellationToken cancellationToken = default)
    {
        try
        {
            var conns = _connectionTracker.GetConnections(userId);
            if (conns.Count == 0) return;
            var groupName = GetGameGroupName(gameId);

            foreach (var conn in conns)
                await _hubContext.Groups.AddToGroupAsync(conn, groupName, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error adding user {userId} to game group {gameId}", userId, gameId);
            throw;
        }
    }

    public async Task LeaveGameGroupForUserAsync(string userId, string gameId, CancellationToken cancellationToken = default)
    {
        try
        {
            var conns = _connectionTracker.GetConnections(userId);
            if (conns.Count == 0) return;
            var groupName = GetGameGroupName(gameId);

            foreach (var conn in conns)
                await _hubContext.Groups.RemoveFromGroupAsync(conn, groupName, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error removing user {userId} from game group {gameId}", userId, gameId);
            throw;
        }
    }
}
