using ChefKnifeStudios.PokerAttack.Client.Core.Services;
using Microsoft.AspNetCore.Components;
using System.Data;

namespace ChefKnifeStudios.PokerAttack.Client.WebApp.Pages;

public partial class Lobbies : ComponentBase
{
    [Inject] ISignalRNotificationService SignalRNotificationService { get; set; } = null!;

    string _playerId = Guid.NewGuid().ToString();

    protected override async Task OnInitializedAsync()
    {
        try
        {
            await SignalRNotificationService.InitAsync();

            SignalRNotificationService.HandleNotificationReceived += async (notification) =>
            {
                await InvokeAsync(() =>
                {
                    Console.WriteLine($"{notification.NotificationType}: {notification.Payload}");
                });
            };
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error: {ex.Message}");
        }
    }
}
