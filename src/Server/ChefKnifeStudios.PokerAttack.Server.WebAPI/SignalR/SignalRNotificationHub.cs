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
public class SignalRNotificationHub(
    ILogger<SignalRNotificationHub> logger,
    IPokerAttackNotificationHelper notificationHelper,
    IGameService gameService,
    ILobbyService lobbyService,
    IServiceScopeFactory serviceScopeFactory) : Hub<ISignalRNotificationClient>
{
    public override async Task OnConnectedAsync()
    {
        await base.OnConnectedAsync();
    }

    // Lobby-wide notifications
    public async Task BroadcastLobbyNotification(PokerAttackNotification notification)
    {
        await notificationHelper.BroadcastToAllAsync(notification);
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

    public async Task StartRound(string lobbyId, string hostId)
    {
        const int _RUN_TIME_IN_SECONDS = 120;

        var lobby = await lobbyService.GetLobbyAsync(lobbyId);
        if (lobby == null 
            || !lobby.HostPlayer.Id.Equals(hostId, StringComparison.InvariantCultureIgnoreCase)) return;

        List<Task> taskList = [];
        foreach (var player in lobby.Players)
        {
            taskList.Add(StartRun(player.Id, _RUN_TIME_IN_SECONDS));
        }
        await Task.WhenAll(taskList);

        // Create background work with its own scope
        _ = Task.Run(async () =>
        {
            await Task.Delay(_RUN_TIME_IN_SECONDS * 1000);

            using var scope = serviceScopeFactory.CreateScope();
            var scopedGameService = scope.ServiceProvider.GetRequiredService<IGameService>();
            var scopedNotificationHelper = scope.ServiceProvider.GetRequiredService<IPokerAttackNotificationHelper>();

            try
            {
                await scopedGameService.EndRoundAsync(lobbyId);
                await scopedNotificationHelper.BroadcastToGameAsync(
                    lobbyId,
                    new PokerAttackNotification(PokerAttackNotificationType.RoundEnded, string.Empty)
                );
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "An error occurred");
            }
        });
    }

    // Start a run (per-player deck)
    async Task StartRun(string playerId, int runTimeInSeconds)
    {
        await gameService.StartPlayerRunAsync(playerId);

        // Deal initial hand
        var initialHand = await gameService.DealHandAsync(playerId, 8);

        var resBody = new RunStartedDTO()
        {
            RunTimeInSeconds = runTimeInSeconds,
            Cards = initialHand.Select(x => x.MapToDTO()),
        };

        await notificationHelper.SendToPlayerAsync(playerId, new PokerAttackNotification
        (
            PokerAttackNotificationType.RunStarted,
            JsonSerializer.Serialize(resBody, JsonOptions.Get())
        ));
    }

    // Deal additional cards
    public async Task DealHand(string playerId, int count)
    {
        var hand = await gameService.DealHandAsync(playerId, count);

        await notificationHelper.SendToPlayerAsync(playerId, new PokerAttackNotification
        (
            PokerAttackNotificationType.CardsDealt,
            JsonSerializer.Serialize(hand.Select(x => x.MapToDTO()), JsonOptions.Get())
        ));
    }

    // Play a hand and report score
    public async Task PlayHand(string playerId, List<CardDTO> hand)
    {
        var result = await gameService.PlayHandAsync(playerId, hand);
        var totalPlayerScore = await gameService.GetPlayerScoreAsync(playerId);

        if (result is null) return;

        await notificationHelper.SendToPlayerAsync(playerId, new PokerAttackNotification
        (
            PokerAttackNotificationType.HandPlayed,
            JsonSerializer.Serialize(result.MapToDTO(totalPlayerScore ?? 0), JsonOptions.Get())
        ));
    }
}
