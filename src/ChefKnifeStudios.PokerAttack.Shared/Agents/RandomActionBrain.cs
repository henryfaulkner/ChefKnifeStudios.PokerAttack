using ChefKnifeStudios.PokerAttack.Shared.DTOs.Agent;
using ChefKnifeStudios.PokerAttack.Shared.DTOs.Gameplay;
using ChefKnifeStudios.PokerAttack.Shared.Enums;

namespace ChefKnifeStudios.PokerAttack.Shared.Agents;

/// <summary>
/// Simple random decision-making brain for testing and baseline AI behavior
/// </summary>
public class RandomActionBrain : IAgentBrain
{
    public string Name => "Random Agent";

    public AgentAction? DecideAction(AgentGameState gameState, Guid localPlayerId)
    {
        return gameState.CurrentGameState switch
        {
            GameStates.InGame => DecideInGameAction(gameState),
            GameStates.Freebie => DecideFreebieAction(gameState),
            GameStates.Shop => DecideShopAction(gameState),
            _ => new AgentAction.WaitAction()
        };
    }

    private AgentAction? DecideInGameAction(AgentGameState state)
    {
        var actions = new List<(AgentAction action, int weight)>();

        // Prioritize playing hands (70% weight)
        if (state.AvailablePlayHands > 0 && state.CardsInHand.Count >= 1)
        {
            var randomHand = SelectRandomHand(state.CardsInHand);
            actions.Add((new AgentAction.PlayHandAction(randomHand), 70));
        }

        // Discard option (20% weight)
        if (state.AvailableDiscards > 0 && state.CardsInHand.Count >= 1)
        {
            var randomDiscard = SelectRandomDiscard(state.CardsInHand);
            actions.Add((new AgentAction.DiscardAction(randomDiscard), 20));
        }

        // Activate power (10% weight)
        if (state.PowerCharges > 0 && state.ActivePlayerPower != null)
        {
            actions.Add((new AgentAction.ActivatePowerAction(), 10));
        }

        // If no actions available, wait
        if (actions.Count == 0)
            return new AgentAction.WaitAction();

        return WeightedRandom(actions);
    }

    private AgentAction? DecideFreebieAction(AgentGameState state)
    {
        // Select a random power from available powers
        if (state.AvailablePlayerPowers != null && state.AvailablePlayerPowers.Count > 0)
        {
            var randomPower = state.AvailablePlayerPowers[Random.Shared.Next(state.AvailablePlayerPowers.Count)];
            return new AgentAction.SelectPlayerPowerAction(randomPower.Id);
        }

        return new AgentAction.WaitAction();
    }

    private AgentAction? DecideShopAction(AgentGameState state)
    {
        // Get affordable items
        if (state.AvailableShopItems != null && state.AvailableShopItems.Count > 0)
        {
            var affordableItems = state.AvailableShopItems
                .Where(item => item.AdjustedPrice <= state.Wallet)
                .ToList();

            // 50% chance to purchase if affordable items exist
            if (affordableItems.Count > 0 && Random.Shared.Next(100) < 50)
            {
                var randomItem = affordableItems[Random.Shared.Next(affordableItems.Count)];
                return new AgentAction.PurchaseShopItemAction(randomItem.Item.Id);
            }
        }

        return new AgentAction.WaitAction();
    }

    private List<CardDTO> SelectRandomHand(IReadOnlyList<CardDTO> cardsInHand)
    {
        // Select 1 to 5 random cards for a hand
        var handSize = Random.Shared.Next(1, Math.Min(6, cardsInHand.Count + 1));
        return cardsInHand
            .OrderBy(_ => Random.Shared.Next())
            .Take(handSize)
            .ToList();
    }

    private List<CardDTO> SelectRandomDiscard(IReadOnlyList<CardDTO> cardsInHand)
    {
        // Discard 1 to 3 random cards
        var discardCount = Random.Shared.Next(1, Math.Min(4, cardsInHand.Count + 1));
        return cardsInHand
            .OrderBy(_ => Random.Shared.Next())
            .Take(discardCount)
            .ToList();
    }

    private AgentAction? WeightedRandom(List<(AgentAction action, int weight)> actions)
    {
        if (actions.Count == 0)
            return null;

        var totalWeight = actions.Sum(a => a.weight);
        var randomValue = Random.Shared.Next(totalWeight);

        var currentWeight = 0;
        foreach (var (action, weight) in actions)
        {
            currentWeight += weight;
            if (randomValue < currentWeight)
                return action;
        }

        // Fallback to first action
        return actions[0].action;
    }
}
