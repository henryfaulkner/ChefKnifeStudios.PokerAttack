using ChefKnifeStudios.PokerAttack.Shared.Enums;

namespace ChefKnifeStudios.PokerAttack.Server.Core.Models;

public class HandResult
{
    public PokerHandType HandType { get; init; }
    public int BaseChips { get; init; }
    public int BaseMultiplier { get; init; }
}
