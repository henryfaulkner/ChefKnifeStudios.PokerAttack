using ChefKnifeStudios.PokerAttack.Shared.DTOs.SignalR;

namespace ChefKnifeStudios.PokerAttack.Server.Core.Interfaces;

/// <summary>
/// Contract for sending PokerAttack game notifications via SignalR.
/// </summary>
public interface IPokerAttackNotificationHelper
{
    /// <summary>
    /// Send a game notification to a specific player.
    /// </summary>
    Task SendToPlayerAsync(string playerId, PokerAttackNotification notification, CancellationToken cancellationToken = default);

    /// <summary>
    /// Broadcast a notification to all players in a given lobby.
    /// </summary>
    Task BroadcastToLobbyAsync(string lobbyId, PokerAttackNotification notification, CancellationToken cancellationToken = default);

    /// <summary>
    /// Broadcast a notification to all players in a given game.
    /// </summary>
    Task BroadcastToGameAsync(string gameId, PokerAttackNotification notification, CancellationToken cancellationToken = default);

    /// <summary>
    /// Send a notification to a subset of players in a given game.
    /// </summary>
    Task SendToPlayersAsync(string gameId, IEnumerable<string> playerIds, PokerAttackNotification notification, CancellationToken cancellationToken = default);

    /// <summary>
    /// Send a notification to all players
    /// </summary>
    Task BroadcastToAllAsync(PokerAttackNotification notification, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get or create the SignalR group name for a lobby.
    /// </summary>
    string GetLobbyGroupName(string lobbyId);

    /// <summary>
    /// Get or create the SignalR group name for a game.
    /// </summary>
    string GetGameGroupName(string gameId);
}
