using ChefKnifeStudios.PokerAttack.Shared.DTOs.Gameplay;
using ChefKnifeStudios.PokerAttack.Shared.Enums;

namespace ChefKnifeStudios.PokerAttack.Shared.DTOs.Agent;

/// <summary>
/// Immutable snapshot of game state for agent decision-making
/// </summary>
/// <param name="CurrentGameState">Current game state (InGame, Freebie, Shop, etc.)</param>
/// <param name="CardsInHand">Cards currently in the player's hand</param>
/// <param name="AvailablePlayHands">Number of hand plays remaining this round</param>
/// <param name="AvailableDiscards">Number of discards remaining this round</param>
/// <param name="PowerCharges">Number of power activations available</param>
/// <param name="ActivePlayerPower">Currently equipped player power, if any</param>
/// <param name="Score">Current score</param>
/// <param name="RunTimeInSeconds">Time elapsed in current run</param>
/// <param name="AvailableShopItems">Items available for purchase (Shop state)</param>
/// <param name="AvailablePlayerPowers">Powers available for selection (Freebie state)</param>
/// <param name="Wallet">Available currency for purchases</param>
public record AgentGameState(
    GameStates CurrentGameState,
    IReadOnlyList<CardDTO> CardsInHand,
    int AvailablePlayHands,
    int AvailableDiscards,
    int PowerCharges,
    PlayerPowerDTO? ActivePlayerPower,
    int Score,
    int RunTimeInSeconds,
    IReadOnlyList<ShopItemDTO>? AvailableShopItems,
    IReadOnlyList<PlayerPowerDTO>? AvailablePlayerPowers,
    int Wallet
);
