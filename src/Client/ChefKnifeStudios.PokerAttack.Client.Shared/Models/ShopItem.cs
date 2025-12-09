using ChefKnifeStudios.PokerAttack.Shared;
using ChefKnifeStudios.PokerAttack.Shared.Enums;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ChefKnifeStudios.PokerAttack.Client.Shared.Models;

public partial class ShopItem : ObservableObject
{
    public ShopItem(ItemBase item)
    {
        ItemId = item.Id;
        Name = item.Name;
        Description = item.Description;
        Price = item.Price;
        RarityTier = item.Rarity?.RarityTier;
        WasPurchased = false;
        Root = item;
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
