namespace ChefKnifeStudios.PokerAttack.Shared.DTOs.Gameplay;

/// <summary>
/// DTO containing the current state of a player during an active game.
/// Used for state resynchronization after reconnection.
/// </summary>
public class PlayerStateDTO
{
    public required IEnumerable<CardDTO> CardsInHand { get; init; }
    public int Score { get; init; }
    public int HandsRemaining { get; init; }
    public int DiscardsRemaining { get; init; }
    public int PowerCharges { get; init; }
    public int Wallet { get; init; }
    public PlayerPowerDTO? ActivePlayerPower { get; init; }
}
