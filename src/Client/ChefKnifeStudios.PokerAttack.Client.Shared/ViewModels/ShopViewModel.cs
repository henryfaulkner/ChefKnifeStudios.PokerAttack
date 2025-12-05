using ChefKnifeStudios.PokerAttack.Client.Core.Extensions;
using ChefKnifeStudios.PokerAttack.Client.Core.Services.EndpointServices;
using ChefKnifeStudios.PokerAttack.Client.Shared.Services;
using ChefKnifeStudios.PokerAttack.Shared;
using ChefKnifeStudios.PokerAttack.Shared.Enums;
using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;

namespace ChefKnifeStudios.PokerAttack.Client.Shared.ViewModels;

public partial class ShopItem : ObservableObject
{
    [SetsRequiredMembers]
    public ShopItem(ItemBase item)
    {
        ItemId = item.Id;
        Name = item.Name;
        Description = item.Description;
        Price = item.Price;
        RarityTier = item.Rarity?.RarityTier;
    }

    public required string ItemId { get; init; }
    public required string Name { get; init; }
    public required string Description { get; init; }
    public required int Price { get; init; }
    public RarityTiers? RarityTier { get; init; }
}

public interface IShopViewModel : IViewModel
{
    string GameId { get; }
    bool IsLoading { get; }
    ObservableCollection<ShopItem> Items { get; }
    void Init(string gameId);
    Task LoadItemsAsync(CancellationToken cancellationToken = default);
    Task PurchaseItemAsync(ShopItem shopItem, CancellationToken cancellationToken = default);
}

public partial class ShopViewModel(
    IApplicationViewModel applicationViewModel,
    IShopEndpointsService shopEndpointsService,
    IToastService toastService) : BaseViewModel, IShopViewModel
{
    [ObservableProperty]
    string? _gameId;

    [ObservableProperty]
    bool _isLoading = false;

    [ObservableProperty]
    ObservableCollection<ShopItem> _items = [];

    public void Init(string gameId)
    {
        GameId = gameId;
    }
    
    public async Task LoadItemsAsync(CancellationToken cancellationToken = default)
    {
        IsLoading = true;
        Items = (await shopEndpointsService.GetShopItemsAsync(cancellationToken))
            .Value?
            .Select(x => new ShopItem(x))
            .ToObservableCollection() ?? [];
        IsLoading = false;
    }

    public async Task PurchaseItemAsync(ShopItem item, CancellationToken cancellationToken = default)
    {
        toastService.ShowSuccess("Item is hypothetically purchased.");
    }
}
