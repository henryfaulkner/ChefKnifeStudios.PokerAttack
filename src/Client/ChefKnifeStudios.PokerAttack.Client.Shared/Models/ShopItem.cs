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
    }

    public required string ItemId { get; init; }
    public required string Name { get; init; }
    public required string Description { get; init; }
    public required int Price { get; init; }
    public RarityTiers? RarityTier { get; init; }
}
