using ChefKnifeStudios.PokerAttack.Client.Shared.Models;
using ChefKnifeStudios.PokerAttack.Client.Shared.Services;
using Microsoft.AspNetCore.Components;
using System.Collections.Specialized;
using System.ComponentModel;

namespace ChefKnifeStudios.PokerAttack.Client.Shared.Components.Gameplay;

public partial class Shop : ComponentBase
{
    [Inject] IGameDataService GameDataService { get; set; } = null!;
    [Inject] IGameDataStore GameDataStore { get; set; } = null!;

    readonly string[] _subscriptions =
    [
        nameof(IGameDataStore.IsLoadingShop),
        nameof(IGameDataStore.ShopItems),
    ];

    protected override void OnInitialized()
    {
        base.OnInitialized();
        GameDataStore.PropertyChanged += ViewModel_OnPropertyChanged;
        GameDataStore.ShopItems.CollectionChanged += HandleCollectionChanged;
        foreach (var item in GameDataStore.ShopItems)
        {
            if (item is INotifyPropertyChanged npc)
                npc.PropertyChanged += HandleItemPropertyChanged;
        }

        _ = GameDataService.LoadShopItemsAsync();
    }

    public void Dispose()
    {
        GameDataStore.PropertyChanged -= ViewModel_OnPropertyChanged;
        GameDataStore.ShopItems.CollectionChanged -= HandleCollectionChanged;
        foreach (var item in GameDataStore.ShopItems)
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

    async Task HandleItemPurchased(ShopItem shopItem)
    {
        await GameDataService.PurchaseShopItemAsync(shopItem);
        await InvokeAsync(StateHasChanged);
    }
}
