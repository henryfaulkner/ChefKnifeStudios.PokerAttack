using ChefKnifeStudios.PokerAttack.Server.Core.Interfaces;
using ChefKnifeStudios.PokerAttack.Shared.DTOs.SignalR;
using Microsoft.AspNetCore.SignalR;

namespace ChefKnifeStudios.PokerAttack.Server.WebAPI.SignalR;

public class PokerAttackNotificationHelper : IPokerAttackNotificationHelper
{
    readonly IHubContext<SignalRNotificationHub, ISignalRNotificationClient> _hubContext;
    readonly IPlayerConnectionTracker _connectionTracker;

    // Cache group names (lobbyId → group)
    readonly Dictionary<string, string> _lobbyGroups = new();
    // Cache group names (gameId → group)
    readonly Dictionary<string, string> _gameGroups = new();

    public PokerAttackNotificationHelper(
        IHubContext<SignalRNotificationHub, ISignalRNotificationClient> hubContext,
        IPlayerConnectionTracker connectionTracker)
    {
        _hubContext = hubContext;
        _connectionTracker = connectionTracker;
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
        catch { /* log or swallow */ }
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
        catch { /* log or swallow */ }
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
        catch { /* log or swallow */ }
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
        catch { /* log or swallow */ }
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
        catch { /* log or swallow */ }
    }

    /// <summary>
    /// Get or generate the SignalR group name for a lobby
    /// </summary>
    public string GetLobbyGroupName(string lobbyId)
    {
        if (_gameGroups.TryGetValue(lobbyId, out var groupName))
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
        catch { /* log or swallow */ }
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
        catch { /* log or swallow */ }
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
        catch { /* log or swallow */ }
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
        catch { /* log or swallow */ }
    }
}
