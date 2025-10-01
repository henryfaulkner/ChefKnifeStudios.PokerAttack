using System.ComponentModel;

namespace ChefKnifeStudios.PokerAttack.Shared.Enums;

public enum PokerHandType
{
    [Description("High Card")]
    HighCard,
    [Description("Pair")]
    Pair,
    [Description("Two Pair")]
    TwoPair,
    [Description("Three of a Kind")]
    ThreeOfAKind,
    [Description("Straight")]
    Straight,
    [Description("Flush")]
    Flush,
    [Description("Full House")]
    FullHouse,
    [Description("Four of a Kind")]
    FourOfAKind,
    [Description("Straight Flush")]
    StraightFlush,
    // RoyalFlush can map to StraightFlush in many cases
}