namespace ChefKnifeStudios.PokerAttack.Shared.DTOs;

public class ShopItemDTO
{
    public required ItemBase Item { get; init; }
    public required int AdjustedPrice { get; init; }
}
