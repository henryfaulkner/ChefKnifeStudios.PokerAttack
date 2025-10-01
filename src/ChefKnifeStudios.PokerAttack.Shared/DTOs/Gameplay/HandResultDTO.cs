using ChefKnifeStudios.PokerAttack.Shared.Enums;

namespace ChefKnifeStudios.PokerAttack.Shared.DTOs.Gameplay;

public class HandResultDTO
{
    public PokerHandType HandType { get; init; }
    public required int[] CardValues { get; init; }
    public int BaseChips { get; init; }
    public int BaseMultiplier { get; init; }
    public int HandScore { get; init; }
    public int TotalPlayerScore { get; init; }
}
