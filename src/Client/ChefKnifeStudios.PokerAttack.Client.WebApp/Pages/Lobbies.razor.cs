using ChefKnifeStudios.PokerAttack.Client.Shared.ViewModels;
using Microsoft.AspNetCore.Components;

namespace ChefKnifeStudios.PokerAttack.Client.WebApp.Pages;

public partial class Lobbies : ComponentBase
{
    [SupplyParameterFromQuery] public required string? GameResult { get; set; }

    [Inject] IApplicationViewModel ApplicationViewModel { get; set; } = null!;
    [Inject] ILobbyViewModel LobbyViewModel { get; set; } = null!;

    void HandleCreateLobbyPressed()
    {
        _ = LobbyViewModel.CreateLobbyAsync(ApplicationViewModel.Player);
    }

    void HandleCloseModalPressed()
    {
        GameResult = null;
        StateHasChanged();
    }
}
