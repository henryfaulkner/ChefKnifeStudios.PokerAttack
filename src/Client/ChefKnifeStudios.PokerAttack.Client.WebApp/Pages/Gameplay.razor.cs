using ChefKnifeStudios.PokerAttack.Client.Shared.Services;
using ChefKnifeStudios.PokerAttack.Client.Shared.ViewModels;
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

    readonly string[] _subscriptions =
    [
        nameof(IGameplayViewModel.RunTimeInSeconds),
        nameof(IGameplayViewModel.Score),
        nameof(IGameplayViewModel.CardsInHand),
        nameof(IGameStateMachineViewModel.GameState),
    ];

    protected override void OnInitialized()
    {
        base.OnInitialized();

        InputService.RegisterKeyAction("q", () => GameplayViewModel.PlaySelectedCardsAsync(ApplicationViewModel.Player.Id));
        InputService.RegisterKeyAction("w", () => GameplayViewModel.DiscardSelectedCardsAsync(ApplicationViewModel.Player.Id));
        InputService.RegisterKeyAction("1", () => ToggleCardSelectionAsync(0));
        InputService.RegisterKeyAction("2", () => ToggleCardSelectionAsync(1));
        InputService.RegisterKeyAction("3", () => ToggleCardSelectionAsync(2));
        InputService.RegisterKeyAction("4", () => ToggleCardSelectionAsync(3));
        InputService.RegisterKeyAction("5", () => ToggleCardSelectionAsync(4));
        InputService.RegisterKeyAction("6", () => ToggleCardSelectionAsync(5));
        InputService.RegisterKeyAction("7", () => ToggleCardSelectionAsync(6));
        InputService.RegisterKeyAction("8", () => ToggleCardSelectionAsync(7));
    }

    protected override async Task OnInitializedAsync()
    {
        await base.OnInitializedAsync();

        GameplayViewModel.Init(GameId);
        await GameplayViewModel.StartRoundAsync(ApplicationViewModel.Player.Id);

        GameplayViewModel.PropertyChanged += ViewModel_OnPropertyChanged;
        GameplayViewModel.CardsInHand.CollectionChanged += CardsInHand_CollectionChanged;
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
    }

    void CardsInHand_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        InvokeAsync(StateHasChanged);
    }

    void ViewModel_OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_subscriptions.Contains(e.PropertyName) is false) return;
        Task.Run(async () => await InvokeAsync(StateHasChanged));
    }

    void HandlePlayHandPressed() =>
        _ = GameplayViewModel.PlaySelectedCardsAsync(ApplicationViewModel.Player.Id);

    void HandleDiscardPressed() =>
        _ = GameplayViewModel.DiscardSelectedCardsAsync(ApplicationViewModel.Player.Id);

    Task ToggleCardSelectionAsync(int index)
    {
        GameplayViewModel.ToggleCardSelection(index);
        StateHasChanged();
        return Task.CompletedTask;
    }

    static string FormatAsMinutesSeconds(int totalSeconds)
    {
        TimeSpan time = TimeSpan.FromSeconds(totalSeconds);
        return time.ToString(@"mm\:ss");
    }
}
