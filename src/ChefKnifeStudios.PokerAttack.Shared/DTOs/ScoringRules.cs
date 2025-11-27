using ChefKnifeStudios.PokerAttack.Shared.Enums;

namespace ChefKnifeStudios.PokerAttack.Shared.DTOs;

public class ScoringRules
{
    public List<HandTypeScore> HandTypeScores { get; set; } = new();
    public List<CardRankValue> CardRankValues { get; set; } = new();
    public string ScoringFormula { get; set; } = "(Card Values + Base Chips) × Multiplier";
    public List<HandTypeCardContribution> CardContributionRules { get; set; } = new();
}

public class HandTypeScore
{
    public PokerHandType HandType { get; set; }
    public string HandTypeName { get; set; } = string.Empty;
    public int BaseChips { get; set; }
    public int BaseMultiplier { get; set; }
    public string Description { get; set; } = string.Empty;
}

public class CardRankValue
{
    public Ranks Rank { get; set; }
    public string RankName { get; set; } = string.Empty;
    public int PointValue { get; set; }
}

public class HandTypeCardContribution
{
    public PokerHandType HandType { get; set; }
    public string ContributionRule { get; set; } = string.Empty;
}