using Microsoft.AspNetCore.Components;

namespace ChefKnifeStudios.PokerAttack.Client.WebApp.Pages;

public partial class GameOver : ComponentBase
{
    [SupplyParameterFromQuery] public required string Result { get; set; }
    [Inject] NavigationManager NavigationManager { get; set; } = null!;

    void HandleReturnPressed()
    {
        NavigationManager.NavigateTo($"", replace: true);
    }

}
