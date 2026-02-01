using ChefKnifeStudios.PokerAttack.Server.BL.Services;
using ChefKnifeStudios.PokerAttack.Server.Core.Interfaces;
using ChefKnifeStudios.PokerAttack.Server.Core.Models;
using ChefKnifeStudios.PokerAttack.Shared.DTOs.Gameplay;
using ChefKnifeStudios.PokerAttack.Shared.DTOs.Lobby;
using ChefKnifeStudios.PokerAttack.Shared.DTOs.SignalR;
using ChefKnifeStudios.PokerAttack.Shared.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ChefKnifeStudios.PokerAttack.Server.WebAPI.SignalR;

public interface ISignalRNotificationClient
{
    Task ReceivePokerAttackNotification(PokerAttackNotification notification, CancellationToken cancellationToken = default);
}

[AllowAnonymous]
public class SignalRNotificationHub(
    IPokerAttackNotificationHelper notificationHelper,
    IGameService gameService,
    IKeyValueRepository<ActiveGame> activeGameRepository,
    IGameStateMachineService gameStateMachineService,
    IPlayerPowerService playerPowerService,
    IPlayerConnectionTracker connectionTracker,
    ILobbyService lobbyService,
    IKeyValueRepository<Lobby> lobbyRepository,
    IServiceProvider serviceProvider,
    ILogger<SignalRNotificationHub> logger) : Hub<ISignalRNotificationClient>
{
    public override async Task OnConnectedAsync()
    {
        var userId = Context.UserIdentifier;
        var connectionId = Context.ConnectionId;
        if (!string.IsNullOrEmpty(userId))
            connectionTracker.Add(userId, connectionId);

        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var userId = Context.UserIdentifier;
        var connectionId = Context.ConnectionId;

        if (!string.IsNullOrEmpty(userId))
        {
            connectionTracker.Remove(userId, connectionId);

            // Check if player has any remaining connections (multi-tab support)
            var remainingConnections = connectionTracker.GetConnections(userId);
            if (!remainingConnections.Any())
            {
                // Player fully disconnected - clean up from both lobby and game
                logger.LogInformation(
                    "Player fully disconnected (all connections closed): UserId={UserId}",
                    userId);

                // Check lobby first
                var (lobbyId, player) = await FindPlayerLobbyAsync(userId);
                if (!string.IsNullOrEmpty(lobbyId) && player != null)
                {
                    await HandleLobbyDisconnect(userId, lobbyId, player);
                }

                // Check game
                var gameId = await FindPlayerGameAsync(userId);
                if (!string.IsNullOrEmpty(gameId))
                {
                    await HandleGameDisconnect(userId, gameId);
                }

                // If not in lobby or game, just log
                if (string.IsNullOrEmpty(lobbyId) && string.IsNullOrEmpty(gameId))
                {
                    logger.LogInformation(
                        "Player disconnected but not in any lobby or game: UserId={UserId}",
                        userId);
                }
            }
            else
            {
                logger.LogDebug(
                    "Player connection closed but {count} connections remain: UserId={UserId}",
                    remainingConnections.Count, userId);
            }
        }

        await base.OnDisconnectedAsync(exception);
    }

    // Server-wide notifications
    public async Task BroadcastServerNotification(PokerAttackNotification notification)
    {
        await notificationHelper.BroadcastToAllAsync(notification);
    }

    // Lobby group notifications
    public async Task BroadcastLobbyNotification(string lobbyId, PokerAttackNotification notification)
    {
        await notificationHelper.BroadcastToGameAsync(lobbyId, notification);
    }

    public async Task JoinLobbyGroupAsync(string lobbyId)
    {
        var groupName = notificationHelper.GetLobbyGroupName(lobbyId);
        await Groups.AddToGroupAsync(Context.ConnectionId, groupName);
    }

    public async Task LeaveLobbyGroupAsync(string lobbyId)
    {
        var groupName = notificationHelper.GetLobbyGroupName(lobbyId);
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, groupName);
    }

    // Game group notifications
    public async Task BroadcastGameNotification(string gameId, PokerAttackNotification notification)
    {
        await notificationHelper.BroadcastToGameAsync(gameId, notification);
    }

    public async Task JoinGameGroupAsync(string gameId)
    {
        var groupName = notificationHelper.GetGameGroupName(gameId);
        await Groups.AddToGroupAsync(Context.ConnectionId, groupName);
    }

    public async Task LeaveGameGroupAsync(string gameId)
    {
        var groupName = notificationHelper.GetGameGroupName(gameId);
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, groupName);
    }

    // -------------------------
    // Game-specific methods
    // -------------------------
    public async Task StartGame(string gameId, string playerId)
    {
        var gameResult = await activeGameRepository.GetAsync(gameId);
        if (!gameResult.IsSuccess || gameResult.Value is null)
            throw new KeyNotFoundException($"Game not found. Game Id {gameId}");

        var game = gameResult.Value;

        // Only one player should start the round
        var firstPlayer = game.Players.FirstOrDefault();
        if (firstPlayer is null || firstPlayer.Id != playerId)
        {
            return;
        }

        await gameService.StartGameAsync(gameId);
    }

    // Play a hand and report score
    public async Task PlayHand(string playerId, List<CardDTO> hand) =>
        await gameService.PlayHandAsync(playerId, hand);

    // Discard from Cards in Hand
    public async Task Discard(string playerId, List<CardDTO> discardCards) =>
        await gameService.DiscardAsync(playerId, discardCards);

    // Transition Game State and broadcast change
    public async Task TransitionGameState(string gameId, GameEvents gameEvent) => 
        await gameStateMachineService.TransitionAsync(gameId, gameEvent);

    // End game / clear game state
    public async Task EndGame(string gameId) => 
        await gameService.EndGameAsync(gameId);

    // Remove Game Player game
    public async Task LeaveGame(string gameId, string playerId) =>
        await gameService.LeaveGameAsync(gameId, playerId);

    public async Task ActivatePlayerPower(string gameId, string playerId) =>
        await playerPowerService.ActivateAsync(gameId, playerId);

    // -------------------------
    // Disconnect handling helpers
    // -------------------------
    private async Task<(string? lobbyId, PlayerDTO? player)> FindPlayerLobbyAsync(string playerId)
    {
        try
        {
            var allLobbiesResult = await lobbyRepository.GetAllAsync();
            if (!allLobbiesResult.IsSuccess || allLobbiesResult.Value is null)
                return (null, null);

            foreach (var (lobbyId, lobby) in allLobbiesResult.Value)
            {
                // Check if player is in this lobby
                var player = lobby.Players.FirstOrDefault(p => p.Id == playerId);
                if (player != null)
                    return (lobbyId, new PlayerDTO { Id = player.Id, Name = player.Name });

                // Check if player is the host
                if (lobby.HostPlayer?.Id == playerId)
                    return (lobbyId, new PlayerDTO { Id = lobby.HostPlayer.Id, Name = lobby.HostPlayer.Name });
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error finding lobby for player {playerId}", playerId);
        }

        return (null, null);
    }

    private async Task<string?> FindPlayerGameAsync(string playerId)
    {
        try
        {
            var allGamesResult = await activeGameRepository.GetAllAsync();
            if (!allGamesResult.IsSuccess || allGamesResult.Value is null)
                return null;

            foreach (var (gameId, game) in allGamesResult.Value)
            {
                if (game.Players.Any(p => p.Id == playerId))
                    return gameId;
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error finding game for player {playerId}", playerId);
        }

        return null;
    }

    private async Task HandleLobbyDisconnect(string playerId, string lobbyId, PlayerDTO player)
    {
        try
        {
            logger.LogWarning(
                "Player disconnected from lobby. Removing immediately: PlayerId={PlayerId}, PlayerName={PlayerName}, LobbyId={LobbyId}",
                playerId, player.Name, lobbyId);

            // Use scoped service to avoid lifetime issues
            using var scope = serviceProvider.CreateScope();
            var scopedLobbyService = scope.ServiceProvider.GetRequiredService<ILobbyService>();

            // Remove player from lobby
            await scopedLobbyService.LeaveLobbyAsync(lobbyId, player);
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Error removing disconnected player from lobby: PlayerId={PlayerId}, LobbyId={LobbyId}",
                playerId, lobbyId);
        }
    }

    private async Task HandleGameDisconnect(string playerId, string gameId)
    {
        try
        {
            logger.LogInformation(
                "Player disconnected from game. Marking as away (grace period active): PlayerId={PlayerId}, GameId={GameId}",
                playerId, gameId);

            // Use scoped service to avoid lifetime issues
            using var scope = serviceProvider.CreateScope();
            var scopedGameService = scope.ServiceProvider.GetRequiredService<IGameService>();

            // Mark player as "away" instead of removing immediately
            // This gives them a grace period to reconnect
            await scopedGameService.MarkPlayerAwayAsync(gameId, playerId);
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Error marking disconnected player as away: PlayerId={PlayerId}, GameId={GameId}",
                playerId, gameId);
        }
    }

    // -------------------------
    // Reconnection handling
    // -------------------------
    public async Task ReconnectToGame(string gameId)
    {
        var userId = Context.UserIdentifier;
        if (string.IsNullOrEmpty(userId))
        {
            logger.LogWarning("ReconnectToGame called without user identifier");
            return;
        }

        try
        {
            // Use scoped service to avoid lifetime issues
            using var scope = serviceProvider.CreateScope();
            var scopedGameService = scope.ServiceProvider.GetRequiredService<IGameService>();

            var result = await scopedGameService.ReconnectPlayerAsync(gameId, userId);

            if (result.IsSuccess)
            {
                // Add player back to SignalR group
                var groupName = notificationHelper.GetGameGroupName(gameId);
                await Groups.AddToGroupAsync(Context.ConnectionId, groupName);

                logger.LogInformation(
                    "Player reconnected to game: PlayerId={PlayerId}, GameId={GameId}",
                    userId, gameId);
            }
            else
            {
                logger.LogWarning(
                    "Player reconnection failed: PlayerId={PlayerId}, GameId={GameId}, Errors={Errors}",
                    userId, gameId, string.Join(", ", result.Errors));
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Error during player reconnection: PlayerId={PlayerId}, GameId={GameId}",
                userId, gameId);
        }
    }
}
