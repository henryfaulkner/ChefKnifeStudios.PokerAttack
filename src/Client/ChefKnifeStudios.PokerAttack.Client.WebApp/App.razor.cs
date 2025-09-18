using ChefKnifeStudios.PokerAttack.Client.Core.Services;
using ChefKnifeStudios.PokerAttack.Client.Shared.ViewModels;
using Microsoft.AspNetCore.Components;

namespace ChefKnifeStudios.PokerAttack.Client.WebApp;

public partial class App : ComponentBase
{
    [Inject] IApplicationViewModel ApplicationViewModel { get; set; } = null!;
    [Inject] ISignalRNotificationService SignalRNotificationService { get; set; } = null!;

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
