using System.Text.Json.Serialization;
using ChefKnifeStudios.PokerAttack.Shared.Enums;

namespace ChefKnifeStudios.PokerAttack.Server.Core.Models;

[JsonDerivedType(typeof(ItemBase), typeDiscriminator: "base")]
[JsonDerivedType(typeof(ItemScoring), typeDiscriminator: "scoring")]
[JsonDerivedType(typeof(ItemGameplay), typeDiscriminator: "gameplay")]
[JsonDerivedType(typeof(ItemWager), typeDiscriminator: "wager")]
public class ItemBase
{
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("description")]
    public required string Description { get; init; }

    [JsonPropertyName("price")]
    public required int Price { get; init; }

    [JsonPropertyName("rarityTier")]
    public required string RarityTier { get; init; }

    // Populated by RarityTier as foreign key
    public ItemRarity? Rarity { get; set; }
}

public class ItemScoring : ItemBase
{
    [JsonPropertyName("baseScoreBuff")]
    public int BaseScoreBuff { get; init; }

    [JsonPropertyName("baseMultiplierBuff")]
    public int BaseMultiplierBuff { get; init; }

    [JsonPropertyName("handScoreBuff")]
    public IEnumerable<IntValueDto<Hands>> HandScoreBuff { get; init; }

    [JsonPropertyName("rankScoreBuff")]
    public IEnumerable<IntValueDto<Ranks>> RankScoreBuff { get; init; }

    [JsonPropertyName("suitScoreBuff")]
    public IEnumerable<IntValueDto<Suits>> SuitScoreBuff { get; init; }

    [JsonPropertyName("handMultiplierBuff")]
    public IEnumerable<IntValueDto<Hands>> HandMultiplierBuff { get; init; }
}

public class ItemGameplay : ItemBase
{
    [JsonPropertyName("handsAvailableBuff")]
    public int HandsAvailableBuff { get; init; }

    [JsonPropertyName("discardsAvailableBuff")]
    public int DiscardsAvailableBuff { get; init; }
}

public class ItemWager : ItemBase
{
    [JsonPropertyName("challengeType")]
    public WagerChallengeType ChallengeType { get; init; }

    [JsonPropertyName("challengeValue")]
    public int ChallengeValue { get; init; }

    [JsonPropertyName("rewardChips")]
    public int RewardChips { get; init; }
}

public enum WagerChallengeType
{
    Hands,
    Score,
    Multiplier,
    SpecificHand,   // e.g., “make a Flush”
    SpecificRank,   // e.g., “play a hand containing a King”
    SpecificSuit    // e.g., “win a hand involving Hearts”
}

public class IntValueDto<T>
{
    [JsonPropertyName("key")]
    public T Key { get; set; }

    [JsonPropertyName("value")]
    public int Value { get; set; }
}

public class ItemRarity
{
	[JsonPropertyName("tier")]
	public required string Tier { get; set; }

	[JsonPropertyName("chance")]
	public int Chance { get; set; }

	[JsonPropertyName("color")]
	public required string Color { get; set; }
}

public static class ItemJsonConstants
{
    public const string ItemSchema =
        """
        [
            {
                "type": "scoring",
                "id": "buff_base_score_small",
                "name": "Score Pebble",
                "description": "Adds a small base score bonus.",
                "price": 10,
                "rarityTier": "common",
                "baseScoreBuff": 5,
                "baseMultiplierBuff": 0,
                "handScoreBuff": [],
                "rankScoreBuff": [],
                "suitScoreBuff": [],
                "handMultiplierBuff": []
            },

            {
                "type": "scoring",
                "id": "buff_multiplier_small",
                "name": "Multiplier Spark",
                "description": "Adds a small multiplier bonus.",
                "price": 15,
                "rarityTier": "common",
                "baseScoreBuff": 0,
                "baseMultiplierBuff": 1,
                "handScoreBuff": [],
                "rankScoreBuff": [],
                "suitScoreBuff": [],
                "handMultiplierBuff": []
            },

            {
                "type": "scoring",
                "id": "buff_flush_score",
                "name": "Flush Chip",
                "description": "Adds score to Flush hands.",
                "price": 20,
                "rarityTier": "uncommon",
                "baseScoreBuff": 0,
                "baseMultiplierBuff": 0,
                "handScoreBuff": [
                    { "key": "Flush", "value": 20 }
                ],
                "rankScoreBuff": [],
                "suitScoreBuff": [],
                "handMultiplierBuff": []
            },

            {
                "type": "scoring",
                "id": "buff_rank_kings",
                "name": "Crown Gem",
                "description": "Adds score to all Kings.",
                "price": 25,
                "rarityTier": "uncommon",
                "baseScoreBuff": 0,
                "baseMultiplierBuff": 0,
                "handScoreBuff": [],
                "rankScoreBuff": [
                    { "key": "King", "value": 5 }
                ],
                "suitScoreBuff": [],
                "handMultiplierBuff": []
            },

            {
                "type": "scoring",
                "id": "buff_suit_hearts",
                "name": "Heart Token",
                "description": "Adds score when using hearts.",
                "price": 30,
                "rarityTier": "rare",
                "baseScoreBuff": 0,
                "baseMultiplierBuff": 0,
                "handScoreBuff": [],
                "rankScoreBuff": [],
                "suitScoreBuff": [
                    { "key": "Hearts", "value": 4 }
                ],
                "handMultiplierBuff": []
            },

            {
                "type": "scoring",
                "id": "buff_multiplier_straight",
                "name": "Straight Edge",
                "description": "Adds multiplier to Straight hands.",
                "price": 40,
                "rarityTier": "rare",
                "baseScoreBuff": 0,
                "baseMultiplierBuff": 0,
                "handScoreBuff": [],
                "rankScoreBuff": [],
                "suitScoreBuff": [],
                "handMultiplierBuff": [
                    { "key": "Straight", "value": 1 }
                ]
            },

            {
                "type": "gameplay",
                "id": "buff_extra_hand",
                "name": "Bonus Hand",
                "description": "Gain one extra hand per round.",
                "price": 15,
                "rarityTier": "common",
                "handsAvailableBuff": 1,
                "discardsAvailableBuff": 0
            },

            {
                "type": "gameplay",
                "id": "buff_extra_discard",
                "name": "Discard Token",
                "description": "Gain one extra discard per round.",
                "price": 15,
                "rarityTier": "common",
                "handsAvailableBuff": 0,
                "discardsAvailableBuff": 1
            },

            {
                "type": "wager",
                "id": "wager_score_100",
                "name": "Score 100",
                "description": "Score at least 100 next round.",
                "price": 10,
                "rarityTier": "common",
                "challengeType": "Score",
                "challengeValue": 100,
                "rewardChips": 50
            },

            {
                "type": "wager",
                "id": "wager_flush",
                "name": "Flush Attempt",
                "description": "Make a Flush next round.",
                "price": 20,
                "rarityTier": "uncommon",
                "challengeType": "SpecificHand",
                "challengeValue": "Flush",
                "rewardChips": 120
            },

            {
                "type": "wager",
                "id": "wager_play_king",
                "name": "King's Wager",
                "description": "Play a hand containing a King.",
                "price": 15,
                "rarityTier": "common",
                "challengeType": "SpecificRank",
                "challengeValue": "King",
                "rewardChips": 60
            }
        ]
        """;

    public const string RaritySchema =
        """
        [
            {
                "tier": "common",
                "chance": 60,
                "color": "white"      
            },
            {
                "tier": "uncommon",
                "chance": 25,
                "color": "green"      
            },
            {
                "tier": "rare",
                "chance": 10,
                "color": "blue"      
            },
            {
                "tier": "epic",
                "chance": 4,
                "color": "purple"      
            },
            {
                "tier": "legendary",
                "chance": 1,
                "color": "gold"      
            }
        ]
        """;
}