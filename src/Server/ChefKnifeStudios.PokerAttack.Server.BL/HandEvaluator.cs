using ChefKnifeStudios.PokerAttack.Server.BL.Services;
using ChefKnifeStudios.PokerAttack.Server.Core.Models;
using ChefKnifeStudios.PokerAttack.Shared.Enums;

namespace ChefKnifeStudios.PokerAttack.Server.BL;

public static class HandEvaluator
{
    public static HandResult EvaluateHand(IEnumerable<Card> cards)
    {
        // Assuming cards has exactly 5 cards
        var cardList = cards.ToList();
        if (cardList.Count != 5)
            throw new ArgumentException("Must evaluate exactly 5 cards for base scoring.");

        bool isFlush = cardList.All(c => c.Suit == cardList[0].Suit);

        bool isStraight = IsStraight(cardList, out Ranks highestInStraight);

        var groups = cardList
            .GroupBy(c => c.Rank)
            .OrderByDescending(g => g.Count())
            .ThenByDescending(g => g.Key) // for tie-breaks etc.
            .ToList();

        // Determine hand type
        PokerHandType type;
        if (isStraight && isFlush)
        {
            type = PokerHandType.StraightFlush;
        }
        else if (groups[0].Count() == 4)
        {
            type = PokerHandType.FourOfAKind;
        }
        else if (groups[0].Count() == 3 && groups[1].Count() == 2)
        {
            type = PokerHandType.FullHouse;
        }
        else if (isFlush)
        {
            type = PokerHandType.Flush;
        }
        else if (isStraight)
        {
            type = PokerHandType.Straight;
        }
        else if (groups[0].Count() == 3)
        {
            type = PokerHandType.ThreeOfAKind;
        }
        else if (groups[0].Count() == 2 && groups[1].Count() == 2)
        {
            type = PokerHandType.TwoPair;
        }
        else if (groups[0].Count() == 2)
        {
            type = PokerHandType.Pair;
        }
        else
        {
            type = PokerHandType.HighCard;
        }

        // Map to base chips/mult
        var (chips, mult) = GetBaseForHand(type);

        return new HandResult { HandType = type, BaseChips = chips, BaseMultiplier = mult };
    }

    static (int chips, int mult) GetBaseForHand(PokerHandType type)
    {
        return type switch
        {
            PokerHandType.HighCard => (5, 1),
            PokerHandType.Pair => (10, 2),
            PokerHandType.TwoPair => (20, 2),
            PokerHandType.ThreeOfAKind => (30, 3),
            PokerHandType.Straight => (30, 4),
            PokerHandType.Flush => (35, 4),
            PokerHandType.FullHouse => (40, 4),
            PokerHandType.FourOfAKind => (60, 7),
            PokerHandType.StraightFlush => (100, 8),
            _ => throw new InvalidOperationException("Unknown hand type")
        };
    }

    static bool IsStraight(List<Card> cards, out Ranks highest)
    {
        // Sort by rank
        var ordered = cards.Select(c => (int)c.Rank).OrderBy(n => n).ToList();

        // Handle Ace low straight (A,2,3,4,5)
        bool aceLow = ordered.SequenceEqual(new List<int> { 2, 3, 4, 5, 14 });
        if (aceLow)
        {
            highest = Ranks.Five;
            return true;
        }

        // Normal straight
        for (int i = 1; i < ordered.Count; i++)
        {
            if (ordered[i] != ordered[i - 1] + 1)
            {
                highest = default;
                return false;
            }
        }

        highest = (Ranks)ordered.Last();
        return true;
    }
}
