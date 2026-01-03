using ChefKnifeStudios.PokerAttack.Server.BL;
using ChefKnifeStudios.PokerAttack.Server.BL.Services;
using ChefKnifeStudios.PokerAttack.Server.Core.Interfaces;
using ChefKnifeStudios.PokerAttack.Server.Core.Models;
using ChefKnifeStudios.PokerAttack.Shared.DTOs.Gameplay;
using ChefKnifeStudios.PokerAttack.Shared.DTOs.SignalR;
using ChefKnifeStudios.PokerAttack.Shared.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

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
    IPlayerDisconnectionTracker disconnectionTracker) : Hub<ISignalRNotificationClient>
{
    public override async Task OnConnectedAsync()
    {
        var userId = Context.UserIdentifier;
        var connectionId = Context.ConnectionId;
        if (!string.IsNullOrEmpty(userId))
        {
            connectionTracker.Add(userId, connectionId);
            // Cancel any pending disconnection timer if player reconnects
            disconnectionTracker.CancelDisconnectionTimer(userId);
        }

        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var userId = Context.UserIdentifier;
        var connectionId = Context.ConnectionId;
        if (!string.IsNullOrEmpty(userId))
        {
            connectionTracker.Remove(userId, connectionId);

            // Check if player has any remaining connections
            var remainingConnections = connectionTracker.GetConnections(userId);
            if (!remainingConnections.Any())
            {
                // Player is fully disconnected, find their active game and start elimination timer
                var gameId = await FindPlayerGameAsync(userId);
                if (!string.IsNullOrEmpty(gameId))
                {
                    disconnectionTracker.TrackDisconnection(userId, gameId);
                }
            }
        }

        await base.OnDisconnectedAsync(exception);
    }

    private async Task<string?> FindPlayerGameAsync(string playerId)
    {
        try
        {
            // Search through all active games to find which one the player is in
            var allGamesResult = await activeGameRepository.GetAllAsync();
            if (allGamesResult.IsSuccess && allGamesResult.Value is not null)
            {
                foreach (var game in allGamesResult.Value)
                {
                    if (game.Value.Players.Any(p => p.Id == playerId))
                    {
                        return game.Key; // Return gameId
                    }
                }
            }
        }
        catch (Exception)
        {
            // Log error but don't throw
        }
        return null;
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
}
