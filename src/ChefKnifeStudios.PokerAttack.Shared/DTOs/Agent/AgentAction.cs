using ChefKnifeStudios.PokerAttack.Shared.DTOs.Gameplay;

namespace ChefKnifeStudios.PokerAttack.Shared.DTOs.Agent;

/// <summary>
/// Base class for all agent actions using discriminated union pattern
/// </summary>
public abstract record AgentAction
{
    /// <summary>
    /// Action to play a hand of cards
    /// </summary>
    /// <param name="Cards">The cards to play as a hand</param>
    public record PlayHandAction(List<CardDTO> Cards) : AgentAction;

    /// <summary>
    /// Action to discard cards from hand
    /// </summary>
    /// <param name="Cards">The cards to discard</param>
    public record DiscardAction(List<CardDTO> Cards) : AgentAction;

    /// <summary>
    /// Action to activate the player's power
    /// </summary>
    public record ActivatePowerAction : AgentAction;

    /// <summary>
    /// Action to select a player power during Freebie state
    /// </summary>
    /// <param name="PlayerPowerId">The ID of the power to select</param>
    public record SelectPlayerPowerAction(string PlayerPowerId) : AgentAction;

    /// <summary>
    /// Action to purchase an item from the shop
    /// </summary>
    /// <param name="ItemId">The ID of the item to purchase</param>
    public record PurchaseShopItemAction(string ItemId) : AgentAction;

    /// <summary>
    /// Action to wait/do nothing this cycle
    /// </summary>
    public record WaitAction : AgentAction;
}
