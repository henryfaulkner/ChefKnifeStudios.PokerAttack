using ChefKnifeStudios.PokerAttack.Server.Core.Interfaces;
using ChefKnifeStudios.PokerAttack.Shared.DTOs.SignalR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace ChefKnifeStudios.PokerAttack.Server.WebAPI.SignalR;

public interface ISignalRNotificationClient
{
    Task ReceivePokerAttackNotification(PokerAttackNotification notification, CancellationToken cancellationToken = default);
}

[AllowAnonymous]
public class SignalRNotificationHub : Hub<ISignalRNotificationClient>
{
    private readonly IPokerAttackNotificationHelper _notificationHelper;

    public SignalRNotificationHub(IPokerAttackNotificationHelper notificationHelper)
    {
        _notificationHelper = notificationHelper;
    }

    public override async Task OnConnectedAsync()
    {
        await base.OnConnectedAsync();
    }

    public async Task BroadcastLobbyNotification(PokerAttackNotification notification)
    {
        await _notificationHelper.BroadcastToAllAsync(notification);
    }

    public async Task BroadcastGameNotification(string gameId, PokerAttackNotification notification)
    {
        await _notificationHelper.BroadcastToGameAsync(gameId, notification);
    }

    public async Task JoinGameGroup(string gameId)
    {
        var groupName = _notificationHelper.GetGameGroupName(gameId);
        await Groups.AddToGroupAsync(Context.ConnectionId, groupName);
    }
}
