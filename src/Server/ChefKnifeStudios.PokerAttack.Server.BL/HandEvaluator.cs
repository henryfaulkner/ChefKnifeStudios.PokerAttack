using ChefKnifeStudios.PokerAttack.Server.BL.Services;
using ChefKnifeStudios.PokerAttack.Server.Core.Models;
using ChefKnifeStudios.PokerAttack.Shared.Enums;

namespace ChefKnifeStudios.PokerAttack.Server.BL;

public class HandEvaluator
{
    readonly Dictionary<Ranks, int> _cardRankValues;
    readonly Dictionary<Hands, (int chips, int mult)> _handTypeScores;

    public HandEvaluator(IScoringRulesService scoringRulesService)
    {
        var rules = scoringRulesService.GetScoringRules();

        _cardRankValues = rules.CardRankValues
            .ToDictionary(crv => crv.Rank, crv => crv.PointValue);

        _handTypeScores = rules.HandTypeScores
            .ToDictionary(hts => hts.HandType, hts => (hts.BaseChips, hts.BaseMultiplier));
    }

    public HandResult EvaluateHand(IEnumerable<Card> cards)
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

        // Map card ranks to their point values once using the scoring rules
        int[] cardValues = cardList
            .Select(c => _cardRankValues.TryGetValue(c.Rank, out var value) ? value : 0)
            .ToArray();

        Hands type;
        int cardValueSum;

        if (cardList.Count == 5 && isStraight && isFlush)
        {
            type = Hands.StraightFlush;
            // All five cards contribute
            cardValueSum = cardValues.Sum();
        }
        else if (groups[0].Count() == 4)
        {
            type = Hands.FourOfAKind;
            // Sum only the four matching ranks
            var fourRank = groups[0].Key;
            cardValueSum = Enumerable.Range(0, cardList.Count)
                                     .Where(i => cardList[i].Rank == fourRank)
                                     .Sum(i => cardValues[i]);
        }
        else if (cardList.Count == 5 && groups[0].Count() == 3 && groups.Count > 1 && groups[1].Count() == 2)
        {
            type = Hands.FullHouse;
            // Sum the three-of-a-kind and the pair ranks (all five cards contribute)
            var threeRank = groups[0].Key;
            var pairRank = groups[1].Key;
            cardValueSum = Enumerable.Range(0, cardList.Count)
                                     .Where(i => cardList[i].Rank == threeRank || cardList[i].Rank == pairRank)
                                     .Sum(i => cardValues[i]);
        }
        else if (cardList.Count == 5 && isFlush)
        {
            type = Hands.Flush;
            // All five cards contribute
            cardValueSum = cardValues.Sum();
        }
        else if (cardList.Count == 5 && isStraight)
        {
            type = Hands.Straight;
            // All five cards contribute
            cardValueSum = cardValues.Sum();
        }
        else if (groups[0].Count() == 3)
        {
            type = Hands.ThreeOfAKind;
            // Sum only the three matching ranks
            var threeRank = groups[0].Key;
            cardValueSum = Enumerable.Range(0, cardList.Count)
                                     .Where(i => cardList[i].Rank == threeRank)
                                     .Sum(i => cardValues[i]);
        }
        else if (groups[0].Count() == 2 && groups.Count > 1 && groups[1].Count() == 2)
        {
            type = Hands.TwoPair;
            // Sum the two pair ranks (four cards)
            var pair1 = groups[0].Key;
            var pair2 = groups[1].Key;
            cardValueSum = Enumerable.Range(0, cardList.Count)
                                     .Where(i => cardList[i].Rank == pair1 || cardList[i].Rank == pair2)
                                     .Sum(i => cardValues[i]);
        }
        else if (groups[0].Count() == 2)
        {
            type = Hands.Pair;
            // Sum only the pair cards
            var pairRank = groups[0].Key;
            cardValueSum = Enumerable.Range(0, cardList.Count)
                                     .Where(i => cardList[i].Rank == pairRank)
                                     .Sum(i => cardValues[i]);
        }
        else
        {
            type = Hands.HighCard;
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

    (int chips, int mult) GetBaseForHand(Hands type)
    {
        if (_handTypeScores.TryGetValue(type, out var score))
        {
            return score;
        }

        throw new InvalidOperationException("Unknown hand type");
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