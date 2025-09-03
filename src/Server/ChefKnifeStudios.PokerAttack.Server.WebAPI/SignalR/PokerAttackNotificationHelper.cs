using ChefKnifeStudios.PokerAttack.Server.Core.Interfaces;
using ChefKnifeStudios.PokerAttack.Shared.DTOs.SignalR;
using Microsoft.AspNetCore.SignalR;

namespace ChefKnifeStudios.PokerAttack.Server.WebAPI.SignalR;

public class PokerAttackNotificationHelper : IPokerAttackNotificationHelper
{
    private readonly IHubContext<SignalRNotificationHub, ISignalRNotificationClient> _hubContext;

    // Cache group names (gameId → group)
    private readonly Dictionary<string, string> _gameGroups = new();

    public PokerAttackNotificationHelper(IHubContext<SignalRNotificationHub, ISignalRNotificationClient> hubContext)
    {
        _hubContext = hubContext;
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
}
