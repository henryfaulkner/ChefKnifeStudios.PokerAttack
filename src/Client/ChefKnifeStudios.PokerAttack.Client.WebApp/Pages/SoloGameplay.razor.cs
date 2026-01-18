using ChefKnifeStudios.PokerAttack.Client.Shared.Models;
using ChefKnifeStudios.PokerAttack.Client.Shared.Services;
using ChefKnifeStudios.PokerAttack.Client.Shared.Services.JsInterop;
using ChefKnifeStudios.PokerAttack.Client.Shared.ViewModels;
using ChefKnifeStudios.PokerAttack.Shared.Enums;
using Microsoft.AspNetCore.Components;
using System.Collections.Specialized;
using System.ComponentModel;

namespace ChefKnifeStudios.PokerAttack.Client.WebApp.Pages;

public partial class SoloGameplay : ComponentBase, IDisposable, IAsyncDisposable
{
    [Inject] ISoloGameplayViewModel SoloGameplayViewModel { get; set; } = null!;
    [Inject] IApplicationViewModel ApplicationViewModel { get; set; } = null!;
    [Inject] IInputJsInterop InputJsInterop { get; set; } = null!;
    [Inject] IInputService InputService { get; set; } = null!;
    [Inject] NavigationManager NavigationManager { get; set; } = null!;

    readonly string[] _subscriptions =
    [
        nameof(ISoloGameplayViewModel.Phase),
        nameof(ISoloGameplayViewModel.Score),
        nameof(ISoloGameplayViewModel.Wallet),
        nameof(ISoloGameplayViewModel.RoundNumber),
        nameof(ISoloGameplayViewModel.Threshold),
        nameof(ISoloGameplayViewModel.CardsInHand),
        nameof(ISoloGameplayViewModel.AvailablePlayHands),
        nameof(ISoloGameplayViewModel.AvailableDiscards),
        nameof(ISoloGameplayViewModel.IsLoading),
        nameof(ISoloGameplayViewModel.ShopItems),
    ];

    protected override void OnInitialized()
    {
        base.OnInitialized();

        InputService.RegisterKeyAction("q", () => Task.Run(() => HandlePlayHandPressed()));
        InputService.RegisterKeyAction("w", () => Task.Run(() => HandleDiscardPressed()));
        InputService.RegisterKeyAction("e", () => Task.Run(() => HandleEndRoundPressed()));
        InputService.RegisterKeyAction("a", () => Task.Run(() => HandleSortByRankPressed()));
        InputService.RegisterKeyAction("s", () => Task.Run(() => HandleSortBySuitPressed()));
        InputService.RegisterKeyAction("d", () => Task.Run(() => HandleClearSelectionsPressed()));
        InputService.RegisterKeyAction("1", () => Task.Run(() => ToggleCardSelection(0)));
        InputService.RegisterKeyAction("2", () => Task.Run(() => ToggleCardSelection(1)));
        InputService.RegisterKeyAction("3", () => Task.Run(() => ToggleCardSelection(2)));
        InputService.RegisterKeyAction("4", () => Task.Run(() => ToggleCardSelection(3)));
        InputService.RegisterKeyAction("5", () => Task.Run(() => ToggleCardSelection(4)));
        InputService.RegisterKeyAction("6", () => Task.Run(() => ToggleCardSelection(5)));
        InputService.RegisterKeyAction("7", () => Task.Run(() => ToggleCardSelection(6)));
        InputService.RegisterKeyAction("8", () => Task.Run(() => ToggleCardSelection(7)));
    }

    protected override async Task OnInitializedAsync()
    {
        await base.OnInitializedAsync();

        // Start the solo game
        var playerName = ApplicationViewModel.Player?.Name ?? "Player";
        await SoloGameplayViewModel.StartGameAsync(playerName);

        SoloGameplayViewModel.PropertyChanged += ViewModel_OnPropertyChanged;
        SoloGameplayViewModel.CardsInHand.CollectionChanged += CardsInHand_CollectionChanged;
        SoloGameplayViewModel.ShopItems.CollectionChanged += ShopItems_CollectionChanged;
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            await InputJsInterop.RegisterKeyHandlerAsync(async key =>
            {
                await InputService.HandleKeyAsync(key);
            });
        }
    }

    public async ValueTask DisposeAsync()
    {
        await InputJsInterop.DisposeAsync();
    }

    public void Dispose()
    {
        SoloGameplayViewModel.PropertyChanged -= ViewModel_OnPropertyChanged;
        SoloGameplayViewModel.CardsInHand.CollectionChanged -= CardsInHand_CollectionChanged;
        SoloGameplayViewModel.ShopItems.CollectionChanged -= ShopItems_CollectionChanged;
    }

    void CardsInHand_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        InvokeAsync(StateHasChanged);
    }

    void ShopItems_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        InvokeAsync(StateHasChanged);
    }

    void ViewModel_OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_subscriptions.Contains(e.PropertyName) is false) return;
        Task.Run(async () => await InvokeAsync(StateHasChanged));
    }

    void HandlePlayHandPressed()
    {
        if (SoloGameplayViewModel.Phase != SoloGamePhase.InGame) return;
        _ = SoloGameplayViewModel.PlaySelectedCardsAsync();
    }

    void HandleDiscardPressed()
    {
        if (SoloGameplayViewModel.Phase != SoloGamePhase.InGame) return;
        _ = SoloGameplayViewModel.DiscardSelectedCardsAsync();
    }

    void HandleEndRoundPressed()
    {
        if (SoloGameplayViewModel.Phase != SoloGamePhase.InGame) return;
        _ = SoloGameplayViewModel.AdvancePhaseAsync();
    }

    void HandleSortByRankPressed()
    {
        SoloGameplayViewModel.SortByRank();
    }

    void HandleSortBySuitPressed()
    {
        SoloGameplayViewModel.SortBySuit();
    }

    void HandleClearSelectionsPressed()
    {
        SoloGameplayViewModel.ClearSelections();
    }

    void HandleContinuePressed()
    {
        _ = SoloGameplayViewModel.AdvancePhaseAsync();
    }

    void HandleItemPurchased(ShopItem item)
    {
        _ = SoloGameplayViewModel.PurchaseItemAsync(item.ItemId);
    }

    void HandleReturnToMenuPressed()
    {
        NavigationManager.NavigateTo("/");
    }

    void ToggleCardSelection(int index)
    {
        SoloGameplayViewModel.ToggleCardSelection(index);
        StateHasChanged();
    }
}
