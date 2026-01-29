using ChefKnifeStudios.PokerAttack.Client.Core.Services;
using ChefKnifeStudios.PokerAttack.Client.Shared.EventArgs.ModalEvents;
using ChefKnifeStudios.PokerAttack.Client.Shared.Services;
using ChefKnifeStudios.PokerAttack.Client.Shared.Services.JsInterop;
using ChefKnifeStudios.PokerAttack.Client.Shared.ViewModels;
using ChefKnifeStudios.PokerAttack.Shared;
using Microsoft.AspNetCore.Components;
using System.ComponentModel;

namespace ChefKnifeStudios.PokerAttack.Client.WebApp.Pages;

public partial class Lobbies : ComponentBase, IDisposable
{
    [SupplyParameterFromQuery(Name = "multi-gameresult")]
    public string? MultiGameResult { get; set; }

    [Inject] IApplicationViewModel ApplicationViewModel { get; set; } = null!;
    [Inject] ILobbyViewModel LobbyViewModel { get; set; } = null!;
    [Inject] IFeatureFlagService FeatureFlagService { get; set; } = null!;
    [Inject] NavigationManager NavigationManager { get; set; } = null!;
    [Inject] IEventNotificationService EventService { get; set; } = null!;
    [Inject] ISoloGameResultStore SoloGameResultStore { get; set; } = null!;
    [Inject] IScoringRulesViewModel ScoringRulesViewModel { get; set; } = null!;
    [Inject] ITourJsInterop TourJsInterop { get; set; } = null!;

    protected override void OnInitialized()
    {
        SoloGameResultStore.PropertyChanged += SoloGameResultStore_PropertyChanged;
    }

    protected override async void OnAfterRender(bool firstRender)
    {
        base.OnAfterRender(firstRender);
        if (!firstRender) return;

        if (!string.IsNullOrWhiteSpace(MultiGameResult))
        {
            ShowMultiGameResultModal();
        }

        if (SoloGameResultStore.HasResult)
        {
            ShowSoloGameResultModal();
        }

        // POC: Show tour highlighting the CREATE LOBBY button on first render
        // TODO: Add logic to check if user is new (e.g., via LocalStorage)
        await ShowWelcomeTourAsync();
    }

    async Task ShowWelcomeTourAsync()
    {
        // Small delay to ensure DOM is fully rendered
        await Task.Delay(500);

        var tourSteps = new TourStep[]
        {
            new(
                Element: "#editablePlayerName",
                Title: "Your Player Name",
                Description: "This is your display name. Click to edit it or use the refresh icon to get a random name."
            ),
            new(
                Element: "#createLobbyBtn",
                Title: "Create a Lobby",
                Description: "Click here to create a new lobby and invite friends to play Poker Attack together!"
            ),
            new(
                Element: "#playSoloBtn",
                Title: "Play Solo",
                Description: "Want to practice? Play solo to sharpen your skills without waiting for others."
            ),
            new(
                Element: "#helpFab",
                Title: "Need Help?",
                Description: "Click here anytime to learn how to play Poker Attack."
            ),
            new(
                Element: "#settingsFab",
                Title: "Settings",
                Description: "Adjust your game settings, sound preferences, and more."
            )
        };

        await TourJsInterop.StartTourAsync(tourSteps);
    }

    public void Dispose()
    {
        SoloGameResultStore.PropertyChanged -= SoloGameResultStore_PropertyChanged;
    }

    void SoloGameResultStore_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ISoloGameResultStore.HasResult) && SoloGameResultStore.HasResult)
        {
            ShowSoloGameResultModal();
        }
    }

    void HandleCreateLobbyPressed()
    {
        _ = LobbyViewModel.CreateLobbyAsync(ApplicationViewModel.Player);
    }

    void HandlePlaySoloPressed()
    {
        NavigationManager.NavigateToSoloGameplay();
    }

    void HandleShowScoringGuidePressed()
    {
        _ = ScoringRulesViewModel.LoadAsync();

        EventService.PostEvent(this, new ScoringRulesModalEventArgs
        {
            ModalAction = ModalEventArgs.ModalActions.Open
        });
    }

    void ShowMultiGameResultModal()
    {
        if (string.IsNullOrWhiteSpace(MultiGameResult)) return;

        EventService.PostEvent(this, new MultiGameResultModalEventArgs
        {
            ModalAction = ModalEventArgs.ModalActions.Open,
            GameResult = MultiGameResult
        });
    }

    void ShowSoloGameResultModal()
    {
        EventService.PostEvent(this, new SoloGameResultModalEventArgs
        {
            ModalAction = ModalEventArgs.ModalActions.Open
        });
    }
}
