using Ardalis.Result;
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

    public Result<HandResult> EvaluateHand(IEnumerable<Card> cards)
    {
        var cardList = cards.ToList();
        if (cardList.Count < 1 || cardList.Count > 5)
            return Result.Invalid(new ValidationError("Must evaluate between 1 and 5 cards for base scoring."));

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

        // Track which cards contribute to the score
        bool[] contributingCards = new bool[cardList.Count];

        Hands type;
        int cardValueSum;

        if (cardList.Count == 5 && isStraight && isFlush)
        {
            type = Hands.StraightFlush;
            // All five cards contribute
            for (int i = 0; i < cardList.Count; i++) contributingCards[i] = true;
            cardValueSum = cardValues.Sum();
        }
        else if (groups[0].Count() == 4)
        {
            type = Hands.FourOfAKind;
            // Only the four matching ranks contribute
            var fourRank = groups[0].Key;
            cardValueSum = 0;
            for (int i = 0; i < cardList.Count; i++)
            {
                if (cardList[i].Rank == fourRank)
                {
                    contributingCards[i] = true;
                    cardValueSum += cardValues[i];
                }
            }
        }
        else if (cardList.Count == 5 && groups[0].Count() == 3 && groups.Count > 1 && groups[1].Count() == 2)
        {
            type = Hands.FullHouse;
            // All five cards contribute (three + pair)
            var threeRank = groups[0].Key;
            var pairRank = groups[1].Key;
            cardValueSum = 0;
            for (int i = 0; i < cardList.Count; i++)
            {
                if (cardList[i].Rank == threeRank || cardList[i].Rank == pairRank)
                {
                    contributingCards[i] = true;
                    cardValueSum += cardValues[i];
                }
            }
        }
        else if (cardList.Count == 5 && isFlush)
        {
            type = Hands.Flush;
            // All five cards contribute
            for (int i = 0; i < cardList.Count; i++) contributingCards[i] = true;
            cardValueSum = cardValues.Sum();
        }
        else if (cardList.Count == 5 && isStraight)
        {
            type = Hands.Straight;
            // All five cards contribute
            for (int i = 0; i < cardList.Count; i++) contributingCards[i] = true;
            cardValueSum = cardValues.Sum();
        }
        else if (groups[0].Count() == 3)
        {
            type = Hands.ThreeOfAKind;
            // Only the three matching ranks contribute
            var threeRank = groups[0].Key;
            cardValueSum = 0;
            for (int i = 0; i < cardList.Count; i++)
            {
                if (cardList[i].Rank == threeRank)
                {
                    contributingCards[i] = true;
                    cardValueSum += cardValues[i];
                }
            }
        }
        else if (groups[0].Count() == 2 && groups.Count > 1 && groups[1].Count() == 2)
        {
            type = Hands.TwoPair;
            // Only the two pair ranks contribute (four cards)
            var pair1 = groups[0].Key;
            var pair2 = groups[1].Key;
            cardValueSum = 0;
            for (int i = 0; i < cardList.Count; i++)
            {
                if (cardList[i].Rank == pair1 || cardList[i].Rank == pair2)
                {
                    contributingCards[i] = true;
                    cardValueSum += cardValues[i];
                }
            }
        }
        else if (groups[0].Count() == 2)
        {
            type = Hands.Pair;
            // Only the pair cards contribute
            var pairRank = groups[0].Key;
            cardValueSum = 0;
            for (int i = 0; i < cardList.Count; i++)
            {
                if (cardList[i].Rank == pairRank)
                {
                    contributingCards[i] = true;
                    cardValueSum += cardValues[i];
                }
            }
        }
        else
        {
            type = Hands.HighCard;
            // Only the highest card contributes
            var highestRank = cardList.Max(c => c.Rank);
            var index = cardList.FindIndex(c => c.Rank == highestRank);
            if (index >= 0)
            {
                contributingCards[index] = true;
                cardValueSum = cardValues[index];
            }
            else
            {
                cardValueSum = 0;
            }
        }

        var (chips, mult) = GetBaseForHand(type);

        return Result.Success(new HandResult
        {
            HandType = type,
            CardValues = cardValues,
            ContributingCards = contributingCards,
            BaseChips = chips,
            BaseMultiplier = mult,
            HandScore = (cardValueSum + chips) * mult,
        });
    }

    (int chips, int mult) GetBaseForHand(Hands type)
    {
        if (_handTypeScores.TryGetValue(type, out var score))
        {
            return score;
        }

        // This should never happen if hand evaluation logic is correct
        // Return default values rather than throwing
        return (0, 0);
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