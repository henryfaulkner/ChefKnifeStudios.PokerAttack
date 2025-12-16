using ChefKnifeStudios.PokerAttack.Shared;
using ChefKnifeStudios.PokerAttack.Shared.DTOs;
using ChefKnifeStudios.PokerAttack.Shared.Enums;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ChefKnifeStudios.PokerAttack.Client.Shared.Models;

public partial class ShopItem : ObservableObject
{
    public ShopItem(ShopItemDTO shopItemDto)
    {
        ItemId = shopItemDto.Item.Id;
        Name = shopItemDto.Item.Name;
        Description = shopItemDto.Item.Description;
        Price = shopItemDto.AdjustedPrice;
        RarityTier = shopItemDto.Item.Rarity?.RarityTier;
        WasPurchased = false;
        Root = shopItemDto.Item;
    }

    [ObservableProperty]
    string _itemId = string.Empty;

    [ObservableProperty]
    string _name = string.Empty;

    [ObservableProperty]
    string _description = string.Empty;

    [ObservableProperty]
    int _price = 0;

    [ObservableProperty]
    RarityTiers? _rarityTier = null;

    [ObservableProperty]
    bool _wasPurchased = false;

    [ObservableProperty]
    ItemBase? _root = null;
}
