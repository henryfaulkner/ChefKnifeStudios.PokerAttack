namespace ChefKnifeStudios.PokerAttack.Shared.DTOs.Gameplay;

public class RunStartedDTO
{
    public required IEnumerable<CardDTO> Cards { get; init; }
    public int HandsAvailable { get; init; }
    public int DiscardsAvailable { get; init; }
}
