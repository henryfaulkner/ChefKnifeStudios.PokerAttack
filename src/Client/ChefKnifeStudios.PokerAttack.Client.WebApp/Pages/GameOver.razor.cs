using Microsoft.AspNetCore.Components;

namespace ChefKnifeStudios.PokerAttack.Client.WebApp.Pages;

public partial class GameOver : ComponentBase
{
    [SupplyParameterFromQuery] public required string Result { get; set; }
    [SupplyParameterFromQuery] public required string Score { get; set; }
    [Inject] NavigationManager NavigationManager { get; set; } = null!;

    void HandleReturnPressed()
    {
        NavigationManager.NavigateTo($"", replace: true);
    }

}
