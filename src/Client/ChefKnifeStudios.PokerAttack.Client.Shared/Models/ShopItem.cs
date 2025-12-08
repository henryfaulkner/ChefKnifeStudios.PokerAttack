using ChefKnifeStudios.PokerAttack.Shared;
using ChefKnifeStudios.PokerAttack.Shared.Enums;
using CommunityToolkit.Mvvm.ComponentModel;
using System.Diagnostics.CodeAnalysis;

namespace ChefKnifeStudios.PokerAttack.Client.Shared.Models;

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
        WasPurchased = false;
        Root = item;
    }

    [ObservableProperty]
    public required string _itemId = string.Empty;
    [ObservableProperty]
    public required string _name = string.Empty;
    [ObservableProperty]
    public required string _description = string.Empty;
    [ObservableProperty]
    public required int _price = 0;
    [ObservableProperty]
    public RarityTiers? _rarityTier = null;
    [ObservableProperty]
    public bool _wasPurchased = false;
    [ObservableProperty]
    public ItemBase? _root = null;
}
