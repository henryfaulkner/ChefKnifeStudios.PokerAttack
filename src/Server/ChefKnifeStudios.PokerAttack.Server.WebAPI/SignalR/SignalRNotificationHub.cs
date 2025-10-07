using ChefKnifeStudios.PokerAttack.Server.BL;
using ChefKnifeStudios.PokerAttack.Server.BL.Services;
using ChefKnifeStudios.PokerAttack.Server.Core.Interfaces;
using ChefKnifeStudios.PokerAttack.Shared;
using ChefKnifeStudios.PokerAttack.Shared.DTOs.Gameplay;
using ChefKnifeStudios.PokerAttack.Shared.DTOs.SignalR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using System.Linq;
using System.Text.Json;

namespace ChefKnifeStudios.PokerAttack.Server.WebAPI.SignalR;

public interface ISignalRNotificationClient
{
    Task ReceivePokerAttackNotification(PokerAttackNotification notification, CancellationToken cancellationToken = default);
}

[AllowAnonymous]
public class SignalRNotificationHub : Hub<ISignalRNotificationClient>
{
    readonly IPokerAttackNotificationHelper _notificationHelper;
    readonly IGameService _gameService;
    readonly ILobbyService _lobbyService;

    public SignalRNotificationHub(
        IPokerAttackNotificationHelper notificationHelper,
        IGameService gameService,
        ILobbyService lobbyService)
    {
        _notificationHelper = notificationHelper;
        _gameService = gameService;
        _lobbyService = lobbyService;
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

    public async Task JoinGameGroupAsync(string gameId)
    {
        var groupName = _notificationHelper.GetGameGroupName(gameId);
        await Groups.AddToGroupAsync(Context.ConnectionId, groupName);
    }

    public async Task LeaveGameGroupAsync(string gameId)
    {
        var groupName = _notificationHelper.GetGameGroupName(gameId);
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, groupName);
    }

    // -------------------------
    // Game-specific methods
    // -------------------------

    public async Task StartRound(string gameId, string hostId)
    {
        const int _RUN_TIME_IN_SECONDS = 120;

        var lobby = await _lobbyService.GetLobbyAsync(gameId);
        if (lobby == null 
            || !lobby.HostPlayer.Id.Equals(hostId, StringComparison.InvariantCultureIgnoreCase)) return;

        List<Task> taskList = [];
        foreach (var player in lobby.Players)
        {
            taskList.Add(StartRun(player.Id, _RUN_TIME_IN_SECONDS));
        }
        await Task.WhenAll(taskList);

        _ = Task.Run(async () =>
        {
            await Task.Delay(_RUN_TIME_IN_SECONDS * 1000);
            await _notificationHelper.BroadcastToGameAsync(
                gameId,
                new PokerAttackNotification(PokerAttackNotificationType.RoundEnded, string.Empty)
            );
        });
    }

    // Start a run (per-player deck)
    async Task StartRun(string playerId, int runTimeInSeconds)
    {
        await _gameService.StartPlayerRunAsync(playerId);

        // Deal initial hand
        var initialHand = await _gameService.DealHandAsync(playerId, 8);

        var resBody = new RunStartedDTO()
        {
            RunTimeInSeconds = runTimeInSeconds,
            Cards = initialHand.Select(x => x.MapToDTO()),
        };

        await _notificationHelper.SendToPlayerAsync(playerId, new PokerAttackNotification
        (
            PokerAttackNotificationType.RunStarted,
            JsonSerializer.Serialize(resBody, JsonOptions.Get())
        ));
    }

    // Deal additional cards
    public async Task DealHand(string playerId, int count)
    {
        var hand = await _gameService.DealHandAsync(playerId, count);

        await _notificationHelper.SendToPlayerAsync(playerId, new PokerAttackNotification
        (
            PokerAttackNotificationType.CardsDealt,
            JsonSerializer.Serialize(hand.Select(x => x.MapToDTO()), JsonOptions.Get())
        ));
    }

    // Play a hand and report score
    public async Task PlayHand(string playerId, List<CardDTO> hand)
    {
        var result = await _gameService.PlayHandAsync(playerId, hand);
        var totalPlayerScore = await _gameService.GetPlayerScoreAsync(playerId);

        if (result is null) return;

        await _notificationHelper.SendToPlayerAsync(playerId, new PokerAttackNotification
        (
            PokerAttackNotificationType.HandPlayed,
            JsonSerializer.Serialize(result.MapToDTO(totalPlayerScore ?? 0), JsonOptions.Get())
        ));
    }
}
