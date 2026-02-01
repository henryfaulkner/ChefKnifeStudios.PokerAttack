using ChefKnifeStudios.PokerAttack.Shared.DTOs;
using ChefKnifeStudios.PokerAttack.Shared.Enums;

namespace ChefKnifeStudios.PokerAttack.Shared.DTOs.Gameplay;

public class MultiGameStateDTO
{
    public required string GameId { get; init; }
    public required string PlayerId { get; init; }
    public GameStates GameState { get; init; }
    public int RoundNumber { get; init; }
    public int Score { get; init; }
    public int Wallet { get; init; }
    public int HandsRemaining { get; init; }
    public int DiscardsRemaining { get; init; }
    public int PowerCharges { get; init; }
    public required IEnumerable<CardDTO> Cards { get; init; }
    public PlayerPowerDTO? ActivePower { get; init; }
}
