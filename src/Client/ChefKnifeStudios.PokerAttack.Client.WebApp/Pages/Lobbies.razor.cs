using ChefKnifeStudios.PokerAttack.Client.Core.Services;
using ChefKnifeStudios.PokerAttack.Client.Shared.EventArgs;
using ChefKnifeStudios.PokerAttack.Client.Shared.Services;
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

    protected override void OnInitialized()
    {
        SoloGameResultStore.PropertyChanged += SoloGameResultStore_PropertyChanged;
    }

    protected override void OnAfterRender(bool firstRender)
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
