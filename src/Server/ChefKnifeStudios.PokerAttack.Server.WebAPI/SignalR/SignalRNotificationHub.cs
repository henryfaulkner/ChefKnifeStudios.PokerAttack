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
        // Add connection to a game group if query string has a gameId
        var httpContext = Context.GetHttpContext();
        var gameId = httpContext?.Request.Query["gameId"].FirstOrDefault();

        if (!string.IsNullOrEmpty(gameId))
        {
            var groupName = _notificationHelper.GetGameGroupName(gameId);
            await Groups.AddToGroupAsync(Context.ConnectionId, groupName);
        }
        
        await base.OnConnectedAsync();
    }

    public async Task BroadcastGameNotification(string gameId, PokerAttackNotification notification)
    {
        await _notificationHelper.BroadcastToGameAsync(gameId, notification);
    }
}
