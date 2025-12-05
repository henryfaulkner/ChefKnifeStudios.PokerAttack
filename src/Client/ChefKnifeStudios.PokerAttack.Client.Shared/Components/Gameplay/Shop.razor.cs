using ChefKnifeStudios.PokerAttack.Client.Shared.ViewModels;
using Microsoft.AspNetCore.Components;
using System.Collections.Specialized;
using System.ComponentModel;

namespace ChefKnifeStudios.PokerAttack.Client.Shared.Components.Gameplay;

public partial class Shop : ComponentBase
{
    [Parameter] public required string GameId { get; set; }
    [CascadingParameter] public IGameStateMachineViewModel GameStateMachineViewModel { get; set; } = null!;
    [Inject] IShopViewModel ShopViewModel { get; set; } = null!;

    readonly string[] _subscriptions =
    [
        nameof(IShopViewModel.IsLoading),
        nameof(IShopViewModel.Items),
    ];

    protected override void OnInitialized()
    {
        base.OnInitialized();
        ShopViewModel.PropertyChanged += ViewModel_OnPropertyChanged;
        ShopViewModel.Items.CollectionChanged += HandleCollectionChanged;
        foreach (var item in ShopViewModel.Items)
        {
            if (item is INotifyPropertyChanged npc)
                npc.PropertyChanged += HandleItemPropertyChanged;
        }
        GameStateMachineViewModel.PropertyChanged += ViewModel_OnPropertyChanged;

        ShopViewModel.Init(GameId);
        _ = ShopViewModel.LoadItemsAsync();
    }

    public void Dispose()
    {
        ShopViewModel.PropertyChanged -= ViewModel_OnPropertyChanged;
        ShopViewModel.Items.CollectionChanged -= HandleCollectionChanged;
        foreach (var item in ShopViewModel.Items)
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

    void HandleItemPurchased(ShopItem shopItem) =>
        ShopViewModel.PurchaseItemAsync(shopItem);
}
