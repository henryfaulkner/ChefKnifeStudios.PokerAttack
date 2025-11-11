using ChefKnifeStudios.PokerAttack.Client.Core.Services;
using ChefKnifeStudios.PokerAttack.Client.Shared.ViewModels;
using Microsoft.AspNetCore.Components;

namespace ChefKnifeStudios.PokerAttack.Client.WebApp.Pages;

public partial class GameOver : ComponentBase
{
    [SupplyParameterFromQuery] public required string Result { get; set; }
    [SupplyParameterFromQuery] public required string GameId { get; set; }

    [Inject] NavigationManager NavigationManager { get; set; } = null!;
    [Inject] ISignalRNotificationService SignalRNotificationService { get; set; } = null!;
    [Inject] IApplicationViewModel ApplicationViewModel { get; set; } = null!;

    void HandleReturnPressed()
    {
        NavigationManager.NavigateTo($"", replace: true);
    }

}
