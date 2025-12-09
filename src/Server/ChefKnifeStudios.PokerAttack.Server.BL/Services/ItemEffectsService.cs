using ChefKnifeStudios.PokerAttack.Server.Core.Models;
using ChefKnifeStudios.PokerAttack.Shared;
using ChefKnifeStudios.PokerAttack.Shared.Enums;

namespace ChefKnifeStudios.PokerAttack.Server.BL.Services;

public interface IItemEffectsService
{
    HandResult ApplyItemEffects(HandResult baseResult, IEnumerable<Card> cards, IEnumerable<ItemBase> purchasedItems);
    int GetHandsAvailableBuff(IEnumerable<ItemBase> purchasedItems);
    int GetDiscardsAvailableBuff(IEnumerable<ItemBase> purchasedItems);
}

public class ItemEffectsService : IItemEffectsService
{
    public HandResult ApplyItemEffects(HandResult baseResult, IEnumerable<Card> cards, IEnumerable<ItemBase> purchasedItems)
    {
        var cardList = cards.ToList();
        var scoringItems = purchasedItems.OfType<ItemScoring>().ToList();

        if (!scoringItems.Any())
        {
            return baseResult;
        }

        // Start with base values
        int totalChips = baseResult.BaseChips;
        int totalMultiplier = baseResult.BaseMultiplier;
        int cardValueSum = 0;

        // Apply base buffs from all scoring items
        foreach (var item in scoringItems)
        {
            totalChips += item.BaseScoreBuff;
            totalMultiplier += item.BaseMultiplierBuff;

            // Apply hand-specific buffs
            var handScoreBuff = item.HandScoreBuff.FirstOrDefault(h => h.Key == baseResult.HandType);
            if (handScoreBuff != null)
            {
                totalChips += handScoreBuff.Value;
            }

            var handMultBuff = item.HandMultiplierBuff.FirstOrDefault(h => h.Key == baseResult.HandType);
            if (handMultBuff != null)
            {
                totalMultiplier += handMultBuff.Value;
            }
        }

        // Calculate card values with rank and suit buffs
        // Only apply buffs to cards that contributed to the hand type
        for (int i = 0; i < cardList.Count; i++)
        {
            // Skip cards that didn't contribute to the hand
            if (!baseResult.ContributingCards[i])
                continue;

            var card = cardList[i];
            int cardValue = baseResult.CardValues[i];

            foreach (var item in scoringItems)
            {
                // Apply rank buffs
                var rankBuff = item.RankScoreBuff.FirstOrDefault(r => r.Key == card.Rank);
                if (rankBuff != null)
                {
                    cardValue += rankBuff.Value;
                }

                // Apply suit buffs
                var suitBuff = item.SuitScoreBuff.FirstOrDefault(s => s.Key == card.Suit);
                if (suitBuff != null)
                {
                    cardValue += suitBuff.Value;
                }
            }

            cardValueSum += cardValue;
        }

        // Calculate final score
        int finalScore = (cardValueSum + totalChips) * totalMultiplier;

        return new HandResult
        {
            HandType = baseResult.HandType,
            CardValues = baseResult.CardValues,
            ContributingCards = baseResult.ContributingCards,
            BaseChips = totalChips,
            BaseMultiplier = totalMultiplier,
            HandScore = finalScore
        };
    }

    public int GetHandsAvailableBuff(IEnumerable<ItemBase> purchasedItems)
    {
        return purchasedItems
            .OfType<ItemGameplay>()
            .Sum(item => item.HandsAvailableBuff);
    }

    public int GetDiscardsAvailableBuff(IEnumerable<ItemBase> purchasedItems)
    {
        return purchasedItems
            .OfType<ItemGameplay>()
            .Sum(item => item.DiscardsAvailableBuff);
    }
}
