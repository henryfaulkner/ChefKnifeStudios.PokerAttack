using ChefKnifeStudios.PokerAttack.Shared.Enums;
using ChefKnifeStudios.PokerAttack.Shared.DTOs;

namespace ChefKnifeStudios.PokerAttack.Server.BL.Services;

public interface IScoringRulesService
{
    ScoringRules GetScoringRules(CancellationToken cancellationToken = default);
}

public class ScoringRulesService : IScoringRulesService
{
    private static readonly ScoringRules _cachedRules = CreateScoringRules();

    public ScoringRules GetScoringRules(CancellationToken cancellationToken = default)
    {
        return _cachedRules;
    }

    static ScoringRules CreateScoringRules()
    {
        return new ScoringRules
        {
            HandTypeScores = new List<HandTypeScore>
            {
                new() { HandType = PokerHandType.StraightFlush, HandTypeName = "Straight Flush", BaseChips = 100, BaseMultiplier = 8, Description = "Five cards in sequence, all of the same suit" },
                new() { HandType = PokerHandType.FourOfAKind, HandTypeName = "Four of a Kind", BaseChips = 60, BaseMultiplier = 7, Description = "Four cards of the same rank" },
                new() { HandType = PokerHandType.FullHouse, HandTypeName = "Full House", BaseChips = 40, BaseMultiplier = 4, Description = "Three of a kind plus a pair" },
                new() { HandType = PokerHandType.Flush, HandTypeName = "Flush", BaseChips = 35, BaseMultiplier = 4, Description = "Five cards of the same suit" },
                new() { HandType = PokerHandType.Straight, HandTypeName = "Straight", BaseChips = 30, BaseMultiplier = 4, Description = "Five cards in sequence" },
                new() { HandType = PokerHandType.ThreeOfAKind, HandTypeName = "Three of a Kind", BaseChips = 30, BaseMultiplier = 3, Description = "Three cards of the same rank" },
                new() { HandType = PokerHandType.TwoPair, HandTypeName = "Two Pair", BaseChips = 20, BaseMultiplier = 2, Description = "Two different pairs" },
                new() { HandType = PokerHandType.Pair, HandTypeName = "Pair", BaseChips = 10, BaseMultiplier = 2, Description = "Two cards of the same rank" },
                new() { HandType = PokerHandType.HighCard, HandTypeName = "High Card", BaseChips = 5, BaseMultiplier = 1, Description = "No matching cards" }
            },
            CardRankValues = new List<CardRankValue>
            {
                new() { Rank = Ranks.Ace, RankName = "Ace", PointValue = 11 },
                new() { Rank = Ranks.King, RankName = "King", PointValue = 10 },
                new() { Rank = Ranks.Queen, RankName = "Queen", PointValue = 10 },
                new() { Rank = Ranks.Jack, RankName = "Jack", PointValue = 10 },
                new() { Rank = Ranks.Ten, RankName = "Ten", PointValue = 10 },
                new() { Rank = Ranks.Nine, RankName = "Nine", PointValue = 9 },
                new() { Rank = Ranks.Eight, RankName = "Eight", PointValue = 8 },
                new() { Rank = Ranks.Seven, RankName = "Seven", PointValue = 7 },
                new() { Rank = Ranks.Six, RankName = "Six", PointValue = 6 },
                new() { Rank = Ranks.Five, RankName = "Five", PointValue = 5 },
                new() { Rank = Ranks.Four, RankName = "Four", PointValue = 4 },
                new() { Rank = Ranks.Three, RankName = "Three", PointValue = 3 },
                new() { Rank = Ranks.Two, RankName = "Two", PointValue = 2 }
            },
            CardContributionRules = new List<HandTypeCardContribution>
            {
                new() { HandType = PokerHandType.StraightFlush, ContributionRule = "All 5 cards contribute to card value sum" },
                new() { HandType = PokerHandType.FourOfAKind, ContributionRule = "Only the 4 matching cards contribute" },
                new() { HandType = PokerHandType.FullHouse, ContributionRule = "All 5 cards contribute (3 of a kind + pair)" },
                new() { HandType = PokerHandType.Flush, ContributionRule = "All 5 cards contribute to card value sum" },
                new() { HandType = PokerHandType.Straight, ContributionRule = "All 5 cards contribute to card value sum" },
                new() { HandType = PokerHandType.ThreeOfAKind, ContributionRule = "Only the 3 matching cards contribute" },
                new() { HandType = PokerHandType.TwoPair, ContributionRule = "Only the 4 cards in the two pairs contribute" },
                new() { HandType = PokerHandType.Pair, ContributionRule = "Only the 2 matching cards contribute" },
                new() { HandType = PokerHandType.HighCard, ContributionRule = "Only the highest card contributes" }
            }
        };
    }
}