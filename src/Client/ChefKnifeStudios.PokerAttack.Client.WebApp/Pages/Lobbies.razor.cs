using ChefKnifeStudios.PokerAttack.Client.Shared.ViewModels;
using ChefKnifeStudios.PokerAttack.Shared;
using Microsoft.AspNetCore.Components;

namespace ChefKnifeStudios.PokerAttack.Client.WebApp.Pages;

public partial class Lobbies : ComponentBase
{
    [SupplyParameterFromQuery] public required string? GameResult { get; set; }

    [Inject] IApplicationViewModel ApplicationViewModel { get; set; } = null!;
    [Inject] ILobbyViewModel LobbyViewModel { get; set; } = null!;
    [Inject] IFeatureFlagService FeatureFlagService { get; set; } = null!;
    [Inject] ILogger<Lobbies> Logger { get; set; } = null!;
    [Inject] NavigationManager NavigationManager { get; set; } = null!;

    bool _showScoringGuide = false;

    protected override async Task OnInitializedAsync()
    {
        await base.OnInitializedAsync();
    }

    void HandleCreateLobbyPressed()
    {
        _ = LobbyViewModel.CreateLobbyAsync(ApplicationViewModel.Player);
    }

    void HandlePlaySoloPressed()
    {
        NavigationManager.NavigateToSoloGameplay();
    }

    void HandleCloseModalPressed()
    {
        GameResult = null;
        StateHasChanged();
    }

    void HandleShowScoringGuidePressed()
    {
        _showScoringGuide = true;
        StateHasChanged();
    }

    void HandleCloseScoringGuidePressed()
    {
        _showScoringGuide = false; 
        StateHasChanged();
    }
}
