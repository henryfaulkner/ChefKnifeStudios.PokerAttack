using ChefKnifeStudios.PokerAttack.Shared.Enums;

namespace ChefKnifeStudios.PokerAttack.Shared.DTOs;

public class ShopItemDTO 
{
    public required string ItemId { get; init; }
    public required string Name { get; init; }
    public required string Description { get; init; }
    public required int Price { get; init; }
    public required RarityTiers RarityTier { get; init; }
}