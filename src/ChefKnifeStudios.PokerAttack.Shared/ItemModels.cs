using ChefKnifeStudios.PokerAttack.Shared.Enums;
using System.Text.Json.Serialization;

namespace ChefKnifeStudios.PokerAttack.Shared;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
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
    public RarityTiers RarityTier { get; init; }

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

    [JsonPropertyName("isCompleted")]
    public bool IsCompleted { get; set; }
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
	[JsonPropertyName("rarityTier")]
	public RarityTiers RarityTier { get; set; }

	[JsonPropertyName("chance")]
	public int Chance { get; set; }
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
                "price": 150,
                "rarityTier": 1,
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
                "price": 225,
                "rarityTier": 1,
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
                "price": 450,
                "rarityTier": 2,
                "baseScoreBuff": 0,
                "baseMultiplierBuff": 0,
                "handScoreBuff": [
                    { "key": "flush", "value": 20 }
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
                "price": 525,
                "rarityTier": 2,
                "baseScoreBuff": 0,
                "baseMultiplierBuff": 0,
                "handScoreBuff": [],
                "rankScoreBuff": [
                    { "key": "king", "value": 5 }
                ],
                "suitScoreBuff": [],
                "handMultiplierBuff": []
            },

            {
                "type": "scoring",
                "id": "buff_suit_hearts",
                "name": "Heart Token",
                "description": "Adds score when using hearts.",
                "price": 750,
                "rarityTier": 3,
                "baseScoreBuff": 0,
                "baseMultiplierBuff": 0,
                "handScoreBuff": [],
                "rankScoreBuff": [],
                "suitScoreBuff": [
                    { "key": "heart", "value": 4 }
                ],
                "handMultiplierBuff": []
            },

            {
                "type": "scoring",
                "id": "buff_multiplier_straight",
                "name": "Straight Edge",
                "description": "Adds multiplier to Straight hands.",
                "price": 900,
                "rarityTier": 3,
                "baseScoreBuff": 0,
                "baseMultiplierBuff": 0,
                "handScoreBuff": [],
                "rankScoreBuff": [],
                "suitScoreBuff": [],
                "handMultiplierBuff": [
                    { "key": "straight", "value": 1 }
                ]
            },

            {
                "type": "gameplay",
                "id": "buff_extra_hand",
                "name": "Bonus Hand",
                "description": "Gain one extra hand per round.",
                "price": 600,
                "rarityTier": 2,
                "handsAvailableBuff": 1,
                "discardsAvailableBuff": 0
            },

            {
                "type": "gameplay",
                "id": "buff_extra_discard",
                "name": "Discard Token",
                "description": "Gain one extra discard per round.",
                "price": 400,
                "rarityTier": 2,
                "handsAvailableBuff": 0,
                "discardsAvailableBuff": 1
            },

            {
                "type": "wager",
                "id": "wager_score_450",
                "name": "Score 450",
                "description": "Score at least 500 next round to win 450 chips.",
                "price": 150,
                "rarityTier": 1,
                "challengeType": "score",
                "challengeValue": 500,
                "rewardChips": 400
            },

            {
                "type": "wager",
                "id": "wager_flush",
                "name": "Flush Attempt",
                "description": "Make a Flush next round to win 800 chips.",
                "price": 450,
                "rarityTier": 2,
                "challengeType": "specificHand",
                "challengeValue": 0,
                "rewardChips": 800
            },

            {
                "type": "wager",
                "id": "wager_play_king",
                "name": "King's Wager",
                "description": "Play a hand containing a King to win 500 chips.",
                "price": 225,
                "rarityTier": 1,
                "challengeType": "specificRank",
                "challengeValue": 0,
                "rewardChips": 500
            }
        ]
        """;

    public const string RaritySchema =
        """
        [
            {
                "rarityTier": 1,
                "chance": 60
            },
            {
                "rarityTier": 2,
                "chance": 25     
            },
            {
                "rarityTier": 3,
                "chance": 10    
            },
            {
                "rarityTier": 4,
                "chance": 4     
            },
            {
                "rarityTier": 5,
                "chance": 1      
            }
        ]
        """;
}