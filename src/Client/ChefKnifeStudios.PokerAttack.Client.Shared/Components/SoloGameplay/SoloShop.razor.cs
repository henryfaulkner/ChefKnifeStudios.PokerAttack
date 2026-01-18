using ChefKnifeStudios.PokerAttack.Client.Shared.Models;
using ChefKnifeStudios.PokerAttack.Client.Shared.ViewModels;
using Microsoft.AspNetCore.Components;
using System.Collections.Specialized;
using System.ComponentModel;

namespace ChefKnifeStudios.PokerAttack.Client.Shared.Components.SoloGameplay;

public partial class SoloShop : ComponentBase, IDisposable
{
    [Inject] ISoloGameplayViewModel SoloGameplayViewModel { get; set; } = null!;

    protected override void OnInitialized()
    {
        base.OnInitialized();
        SoloGameplayViewModel.ShopItems.CollectionChanged += HandleCollectionChanged;
        foreach (var item in SoloGameplayViewModel.ShopItems)
        {
            if (item is INotifyPropertyChanged npc)
                npc.PropertyChanged += HandleItemPropertyChanged;
        }
    }

    public void Dispose()
    {
        SoloGameplayViewModel.ShopItems.CollectionChanged -= HandleCollectionChanged;
        foreach (var item in SoloGameplayViewModel.ShopItems)
        {
            if (item is INotifyPropertyChanged npc)
                npc.PropertyChanged -= HandleItemPropertyChanged;
        }
    }

    void HandleCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems is not null)
        {
            foreach (var item in e.NewItems)
            {
                if (item is INotifyPropertyChanged npc)
                    npc.PropertyChanged += HandleItemPropertyChanged;
            }
        }

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
        await SoloGameplayViewModel.PurchaseItemAsync(shopItem.ItemId);
        await InvokeAsync(StateHasChanged);
    }
}
