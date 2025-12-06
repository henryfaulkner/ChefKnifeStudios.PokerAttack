using ChefKnifeStudios.PokerAttack.Client.Shared.Models;
using ChefKnifeStudios.PokerAttack.Client.Shared.Services;
using ChefKnifeStudios.PokerAttack.Client.Shared.ViewModels;
using Microsoft.AspNetCore.Components;
using System.Collections.Specialized;
using System.ComponentModel;

namespace ChefKnifeStudios.PokerAttack.Client.Shared.Components.Gameplay;

public partial class PlayerPowerList : ComponentBase
{
    [Parameter] public required string GameId { get; set; }
    [CascadingParameter] public IGameStateMachineViewModel GameStateMachineViewModel { get; set; } = null!;
    [Inject] IGameDataService GameDataService { get; set; } = null!;
    [Inject] IGameDataStore GameDataStore { get; set; } = null!;

    readonly string[] _subscriptions =
    [
        nameof(IGameDataStore.IsLoadingPlayerPowers),
        nameof(IGameDataStore.PlayerPowers),
        nameof(IGameStateMachineViewModel.GameState),
    ];

    protected override void OnInitialized()
    {
        base.OnInitialized();
        GameDataStore.PropertyChanged += ViewModel_OnPropertyChanged;
        GameDataStore.PlayerPowers.CollectionChanged += HandleCollectionChanged;
        foreach (var item in GameDataStore.PlayerPowers)
        {
            if (item is INotifyPropertyChanged npc)
                npc.PropertyChanged += HandleItemPropertyChanged;
        }
        GameStateMachineViewModel.PropertyChanged += ViewModel_OnPropertyChanged;

        _ = GameDataService.LoadPlayerPowersAsync();
    }

    public void Dispose()
    {
        GameDataStore.PropertyChanged -= ViewModel_OnPropertyChanged;
        GameDataStore.PlayerPowers.CollectionChanged -= HandleCollectionChanged;
        foreach (var item in GameDataStore.PlayerPowers)
        {
            if (item is INotifyPropertyChanged npc)
                npc.PropertyChanged -= HandleItemPropertyChanged;
        }
        GameStateMachineViewModel.PropertyChanged -= ViewModel_OnPropertyChanged;
    }

    void ViewModel_OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is null || _subscriptions.Contains(e.PropertyName) is false) return;
        Task.Run(async () => await InvokeAsync(StateHasChanged));
    }

    void HandleCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        // New items added?
        if (e.NewItems is not null)
        {
            foreach (var item in e.NewItems)
            {
                if (item is INotifyPropertyChanged npc)
                    npc.PropertyChanged += HandleItemPropertyChanged;
            }
        }

        // Items removed?
        if (e.OldItems is not null)
        {
            foreach (var item in e.OldItems)
            {
                if (item is INotifyPropertyChanged npc)
                    npc.PropertyChanged -= HandleItemPropertyChanged;
            }
        }

        InvokeAsync(StateHasChanged);
    }

    void HandleItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        InvokeAsync(StateHasChanged);
    }

    void HandlePowerSelected(PlayerPowerListItem playerPower) =>
        GameDataService.SelectPlayerPowerAsync(playerPower);
}
