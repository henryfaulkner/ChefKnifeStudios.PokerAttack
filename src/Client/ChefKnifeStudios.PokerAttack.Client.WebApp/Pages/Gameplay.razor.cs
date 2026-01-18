using ChefKnifeStudios.PokerAttack.Client.Shared.Models;
using ChefKnifeStudios.PokerAttack.Client.Shared.Services;
using ChefKnifeStudios.PokerAttack.Client.Shared.Services.JsInterop;
using ChefKnifeStudios.PokerAttack.Client.Shared.ViewModels;
using ChefKnifeStudios.PokerAttack.Shared.Enums;
using Microsoft.AspNetCore.Components;
using System.Collections.Specialized;
using System.ComponentModel;

namespace ChefKnifeStudios.PokerAttack.Client.WebApp.Pages;

public partial class Gameplay : ComponentBase, IDisposable, IAsyncDisposable
{
    [SupplyParameterFromQuery] public required string GameId { get; set; }

    [Inject] IApplicationViewModel ApplicationViewModel { get; set; } = null!;
    [Inject] IGameplayViewModel GameplayViewModel { get; set; } = null!;
    [Inject] IGameStateMachineViewModel GameStateMachineViewModel { get; set; } = null!;
    [Inject] IInputService InputService { get; set; } = null!;
    [Inject] IInputJsInterop InputJsInterop { get; set; } = null!;
    [Inject] IGameDataStore GameDataStore { get; set; } = null!;

    readonly string[] _subscriptions =
    [
        nameof(IGameplayViewModel.RunTimeInSeconds),
        nameof(IGameplayViewModel.Score),
        nameof(IGameplayViewModel.CardsInHand),
        nameof(IGameplayViewModel.AvailablePlayHands),
        nameof(IGameplayViewModel.AvailableDiscards),
        nameof(IGameplayViewModel.PowerCharges),
        nameof(IGameStateMachineViewModel.GameState),
        nameof(IGameDataStore.IsLoadingWallet),
        nameof(IGameDataStore.Wallet),
    ];

    protected override void OnInitialized()
    {
        base.OnInitialized();

        InputService.RegisterKeyAction("q", () => Task.Run(() => HandlePlayHandPressed()));
        InputService.RegisterKeyAction("w", () => Task.Run(() => HandleDiscardPressed()));
        InputService.RegisterKeyAction("e", () => Task.Run(() => HandleActivatePowerPressed()));
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

        // Reset and set GameId in GameDataStore
        GameDataStore.Reset();
        GameDataStore.GameId = GameId;

        await GameplayViewModel.StartGameAsync(ApplicationViewModel.Player.Id);

        GameplayViewModel.PropertyChanged += ViewModel_OnPropertyChanged;
        GameplayViewModel.CardsInHand.CollectionChanged += CardsInHand_CollectionChanged;
        GameStateMachineViewModel.PropertyChanged += ViewModel_OnPropertyChanged;
        GameDataStore.PropertyChanged += ViewModel_OnPropertyChanged;
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
        GameplayViewModel.PropertyChanged -= ViewModel_OnPropertyChanged;
        GameplayViewModel.CardsInHand.CollectionChanged -= CardsInHand_CollectionChanged;
        GameStateMachineViewModel.PropertyChanged -= ViewModel_OnPropertyChanged;
        GameDataStore.PropertyChanged -= ViewModel_OnPropertyChanged;
    }

    void CardsInHand_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        InvokeAsync(StateHasChanged);
    }

    void ViewModel_OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_subscriptions.Contains(e.PropertyName) is false) return;
        if (sender is IGameStateMachineViewModel && e.PropertyName == nameof(IGameStateMachineViewModel.GameState))
        {
            if (GameStateMachineViewModel.GameState == GameStates.InGame)
            {
                _ = GameplayViewModel.StartRoundAsync(ApplicationViewModel.Player.Id);
            }
        }
        Task.Run(async () => await InvokeAsync(StateHasChanged));
    }

    void HandlePlayHandPressed()
    {
        if (GameStateMachineViewModel.GameState != GameStates.InGame) return;
        _ = GameplayViewModel.PlaySelectedCardsAsync(ApplicationViewModel.Player.Id);
    }

    void HandleDiscardPressed()
    {
        if (GameStateMachineViewModel.GameState != GameStates.InGame) return;
        _ = GameplayViewModel.DiscardSelectedCardsAsync(ApplicationViewModel.Player.Id);
    }

    void HandleSortByRankPressed()
    {
        if (GameStateMachineViewModel.GameState != GameStates.InGame) return;
        GameplayViewModel.SortByRank();
    }

    void HandleSortBySuitPressed()
    {
        if (GameStateMachineViewModel.GameState != GameStates.InGame) return;
        GameplayViewModel.SortBySuit();
    }

    void HandleClearSelectionsPressed()
    {
        if (GameStateMachineViewModel.GameState != GameStates.InGame) return;
        GameplayViewModel.ClearSelections();
    }

    void HandleActivatePowerPressed()
    {
        if (GameStateMachineViewModel.GameState != GameStates.InGame) return;
        _ = GameplayViewModel.ActivatePlayerPowerAsync(ApplicationViewModel.Player.Id);
    }

    void ToggleCardSelection(int index)
    {
        if (GameStateMachineViewModel.GameState != GameStates.InGame) return;
        GameplayViewModel.ToggleCardSelection(index);
        StateHasChanged();
    }

    static string FormatAsMinutesSeconds(int totalSeconds)
    {
        TimeSpan time = TimeSpan.FromSeconds(totalSeconds);
        return time.ToString(@"mm\:ss");
    }
}
