using Microsoft.AspNetCore.SignalR;

namespace ChefKnifeStudios.PokerAttack.Server.WebAPI.SignalR;

public class PlayerIdProvider : IUserIdProvider
{
    public string? GetUserId(HubConnectionContext connection)
    {
        // Try to get playerId from query string
        return connection.GetHttpContext()?.Request.Query["playerId"].FirstOrDefault();
    }
}
