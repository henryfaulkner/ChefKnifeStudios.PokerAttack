using ChefKnifeStudios.PokerAttack.Server.BL.Services;
using ChefKnifeStudios.PokerAttack.Server.Core.Interfaces;
using ChefKnifeStudios.PokerAttack.Server.Core.Models;
using ChefKnifeStudios.PokerAttack.Shared;
using ChefKnifeStudios.PokerAttack.Shared.DTOs.SignalR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using System.Text.Json;

namespace ChefKnifeStudios.PokerAttack.Server.WebAPI.SignalR;

public interface ISignalRNotificationClient
{
    Task ReceivePokerAttackNotification(PokerAttackNotification notification, CancellationToken cancellationToken = default);
}

[AllowAnonymous]
public class SignalRNotificationHub : Hub<ISignalRNotificationClient>
{
    private readonly IPokerAttackNotificationHelper _notificationHelper;
    private readonly IGameService _gameService;

    public SignalRNotificationHub(
        IPokerAttackNotificationHelper notificationHelper,
        IGameService gameService)
    {
        _notificationHelper = notificationHelper;
        _gameService = gameService;
    }

    public override async Task OnConnectedAsync()
    {
        await base.OnConnectedAsync();
    }

    // Lobby-wide notifications
    public async Task BroadcastLobbyNotification(PokerAttackNotification notification)
    {
        await _notificationHelper.BroadcastToAllAsync(notification);
    }

    // Game group notifications
    public async Task BroadcastGameNotification(string gameId, PokerAttackNotification notification)
    {
        await _notificationHelper.BroadcastToGameAsync(gameId, notification);
    }

    public async Task JoinGameGroup(string gameId)
    {
        var groupName = _notificationHelper.GetGameGroupName(gameId);
        await Groups.AddToGroupAsync(Context.ConnectionId, groupName);
    }

    // -------------------------
    // Game-specific methods
    // -------------------------

    // Start a run (per-player deck)
    public async Task StartRun(string playerId)
    {
        await _gameService.StartPlayerRunAsync(playerId);

        // Deal initial hand
        var initialHand = await _gameService.DealHandAsync(playerId, 5);

        await Clients.Caller.ReceivePokerAttackNotification(new PokerAttackNotification
        (
            PokerAttackNotificationType.RunStarted,
            JsonSerializer.Serialize(initialHand, JsonOptions.Get())
        ));
    }

    // Deal additional cards
    public async Task DealHand(string playerId, int count)
    {
        var hand = await _gameService.DealHandAsync(playerId, count);

        await Clients.Caller.ReceivePokerAttackNotification(new PokerAttackNotification
        (
            PokerAttackNotificationType.CardsDealt,
            JsonSerializer.Serialize(hand, JsonOptions.Get())
        ));
    }

    // Play a hand and report score
    public async Task PlayHand(string playerId, List<Card> hand)
    {
        var result = await _gameService.PlayHandAsync(playerId, hand);
        var totalScore = await _gameService.GetPlayerScoreAsync(playerId);

        await Clients.Caller.ReceivePokerAttackNotification(new PokerAttackNotification
        (
            PokerAttackNotificationType.HandPlayed,
            JsonSerializer.Serialize(new
            {
                result.HandType,
                result.BaseChips,
                result.BaseMultiplier,
                Score = totalScore
            }, JsonOptions.Get())
        ));
    }
}
