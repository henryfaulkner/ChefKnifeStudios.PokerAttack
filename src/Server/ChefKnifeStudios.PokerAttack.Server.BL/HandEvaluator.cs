using ChefKnifeStudios.PokerAttack.Server.BL.Services;
using ChefKnifeStudios.PokerAttack.Server.Core.Models;
using ChefKnifeStudios.PokerAttack.Shared.Enums;

namespace ChefKnifeStudios.PokerAttack.Server.BL;

public static class HandEvaluator
{
    public static HandResult EvaluateHand(IEnumerable<Card> cards)
    {
        var cardList = cards.ToList();
        if (cardList.Count < 1 || cardList.Count > 5)
            throw new ArgumentException("Must evaluate between 1 and 5 cards for base scoring.");

        bool isFlush = cardList.Count == 5 && cardList.All(c => c.Suit == cardList[0].Suit);
        bool isStraight = cardList.Count == 5 && IsStraight(cardList, out Ranks highestInStraight);

        var groups = cardList
            .GroupBy(c => c.Rank)
            .OrderByDescending(g => g.Count())
            .ThenByDescending(g => g.Key)
            .ToList();

        // Map card ranks to their point values once
        int[] cardValues = cardList
            .Select(c => c.Rank switch
            {
                Ranks.Ace => 11,
                Ranks.King or Ranks.Queen or Ranks.Jack => 10,
                Ranks.Ten => 10,
                Ranks.Nine => 9,
                Ranks.Eight => 8,
                Ranks.Seven => 7,
                Ranks.Six => 6,
                Ranks.Five => 5,
                Ranks.Four => 4,
                Ranks.Three => 3,
                Ranks.Two => 2,
                _ => 0
            })
            .ToArray();

        PokerHandType type;
        int cardValueSum;

        if (cardList.Count == 5 && isStraight && isFlush)
        {
            type = PokerHandType.StraightFlush;
            // All five cards contribute
            cardValueSum = cardValues.Sum();
        }
        else if (groups[0].Count() == 4)
        {
            type = PokerHandType.FourOfAKind;
            // Sum only the four matching ranks
            var fourRank = groups[0].Key;
            cardValueSum = Enumerable.Range(0, cardList.Count)
                                     .Where(i => cardList[i].Rank == fourRank)
                                     .Sum(i => cardValues[i]);
        }
        else if (cardList.Count == 5 && groups[0].Count() == 3 && groups.Count > 1 && groups[1].Count() == 2)
        {
            type = PokerHandType.FullHouse;
            // Sum the three-of-a-kind and the pair ranks (all five cards contribute)
            var threeRank = groups[0].Key;
            var pairRank = groups[1].Key;
            cardValueSum = Enumerable.Range(0, cardList.Count)
                                     .Where(i => cardList[i].Rank == threeRank || cardList[i].Rank == pairRank)
                                     .Sum(i => cardValues[i]);
        }
        else if (cardList.Count == 5 && isFlush)
        {
            type = PokerHandType.Flush;
            // All five cards contribute
            cardValueSum = cardValues.Sum();
        }
        else if (cardList.Count == 5 && isStraight)
        {
            type = PokerHandType.Straight;
            // All five cards contribute
            cardValueSum = cardValues.Sum();
        }
        else if (groups[0].Count() == 3)
        {
            type = PokerHandType.ThreeOfAKind;
            // Sum only the three matching ranks
            var threeRank = groups[0].Key;
            cardValueSum = Enumerable.Range(0, cardList.Count)
                                     .Where(i => cardList[i].Rank == threeRank)
                                     .Sum(i => cardValues[i]);
        }
        else if (groups[0].Count() == 2 && groups.Count > 1 && groups[1].Count() == 2)
        {
            type = PokerHandType.TwoPair;
            // Sum the two pair ranks (four cards)
            var pair1 = groups[0].Key;
            var pair2 = groups[1].Key;
            cardValueSum = Enumerable.Range(0, cardList.Count)
                                     .Where(i => cardList[i].Rank == pair1 || cardList[i].Rank == pair2)
                                     .Sum(i => cardValues[i]);
        }
        else if (groups[0].Count() == 2)
        {
            type = PokerHandType.Pair;
            // Sum only the pair cards
            var pairRank = groups[0].Key;
            cardValueSum = Enumerable.Range(0, cardList.Count)
                                     .Where(i => cardList[i].Rank == pairRank)
                                     .Sum(i => cardValues[i]);
        }
        else
        {
            type = PokerHandType.HighCard;
            // Use only the highest card's value
            var highestRank = cardList.Max(c => c.Rank);
            var index = cardList.FindIndex(c => c.Rank == highestRank);
            cardValueSum = index >= 0 ? cardValues[index] : 0;
        }

        var (chips, mult) = GetBaseForHand(type);

        return new HandResult 
        { 
            HandType = type, 
            CardValues = cardValues,
            BaseChips = chips, 
            BaseMultiplier = mult,
            HandScore = (cardValueSum + chips) * mult,
        };
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
        bool aceLow = ordered.SequenceEqual(new List<int> { (int)Ranks.Ace, (int)Ranks.Two, (int)Ranks.Three, (int)Ranks.Four, (int)Ranks.Five, });
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
