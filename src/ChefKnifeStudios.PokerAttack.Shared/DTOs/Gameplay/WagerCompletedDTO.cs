namespace ChefKnifeStudios.PokerAttack.Shared.DTOs.Gameplay;

public class WagerCompletedDTO
{
    public required string WagerId { get; init; }
    public required string WagerName { get; init; }
    public required string WagerDescription { get; init; }
    public int ChipsAwarded { get; init; }
}
