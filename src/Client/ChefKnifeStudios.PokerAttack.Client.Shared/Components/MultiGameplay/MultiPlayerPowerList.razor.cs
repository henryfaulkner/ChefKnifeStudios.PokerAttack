using ChefKnifeStudios.PokerAttack.Client.Shared.Models;
using ChefKnifeStudios.PokerAttack.Client.Shared.Services;
using ChefKnifeStudios.PokerAttack.Client.Shared.ViewModels;
using Microsoft.AspNetCore.Components;
using System.Collections.Specialized;
using System.ComponentModel;

namespace ChefKnifeStudios.PokerAttack.Client.Shared.Components.MultiGameplay;

public partial class MultiPlayerPowerList : ComponentBase
{
    [Inject] IMultiGameDataService MultiGameDataService { get; set; } = null!;
    [Inject] IMultiGameDataStore MultiGameDataStore { get; set; } = null!;
    [Inject] IMultiPlayerPowerSelectionViewModel MultiPlayerPowerSelectionViewModel { get; set; } = null!;

    readonly string[] _subscriptions =
    [
        nameof(IMultiGameDataStore.IsLoadingPlayerPowers),
        nameof(IMultiGameDataStore.PlayerPowers),
        nameof(IMultiPlayerPowerSelectionViewModel.SelectionTimeSeconds),
    ];

    protected override void OnInitialized()
    {
        base.OnInitialized();
        MultiGameDataStore.PropertyChanged += ViewModel_OnPropertyChanged;
        MultiPlayerPowerSelectionViewModel.PropertyChanged += ViewModel_OnPropertyChanged;
        MultiGameDataStore.PlayerPowers.CollectionChanged += HandleCollectionChanged;
        foreach (var item in MultiGameDataStore.PlayerPowers)
        {
            if (item is INotifyPropertyChanged npc)
                npc.PropertyChanged += HandleItemPropertyChanged;
        }

        _ = MultiGameDataService.LoadPlayerPowersAsync();
    }

    public void Dispose()
    {
        MultiGameDataStore.PropertyChanged -= ViewModel_OnPropertyChanged;
        MultiPlayerPowerSelectionViewModel.PropertyChanged -= ViewModel_OnPropertyChanged;
        MultiGameDataStore.PlayerPowers.CollectionChanged -= HandleCollectionChanged;
        foreach (var item in MultiGameDataStore.PlayerPowers)
        {
            if (item is INotifyPropertyChanged npc)
                npc.PropertyChanged -= HandleItemPropertyChanged;
        }
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

    async Task HandlePowerSelected(PlayerPowerListItem playerPower)
    {
        await MultiGameDataService.SelectPlayerPowerAsync(playerPower);
        await InvokeAsync(StateHasChanged);
    }
}
