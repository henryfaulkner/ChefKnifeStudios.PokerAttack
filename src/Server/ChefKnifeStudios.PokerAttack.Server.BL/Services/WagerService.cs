using ChefKnifeStudios.PokerAttack.Server.Core.Models;
using ChefKnifeStudios.PokerAttack.Shared;
using ChefKnifeStudios.PokerAttack.Shared.Enums;

namespace ChefKnifeStudios.PokerAttack.Server.BL.Services;

public interface IWagerService
{
    List<WagerResult> CheckWagers(
        IEnumerable<ItemWager> activeWagers,
        HandResult handResult,
        IEnumerable<Card> cards);
}

public class WagerService : IWagerService
{
    public List<WagerResult> CheckWagers(
        IEnumerable<ItemWager> activeWagers,
        HandResult handResult,
        IEnumerable<Card> cards)
    {
        var results = new List<WagerResult>();
        var cardList = cards.ToList();

        foreach (var wager in activeWagers)
        {
            bool isCompleted = wager.ChallengeType switch
            {
                WagerChallengeType.Score => handResult.HandScore >= wager.ChallengeValue,
                WagerChallengeType.Multiplier => handResult.BaseMultiplier >= wager.ChallengeValue,
                WagerChallengeType.SpecificHand => handResult.HandType == (Hands)wager.ChallengeValue,
                WagerChallengeType.SpecificRank => cardList.Any(c => (int)c.Rank == wager.ChallengeValue),
                WagerChallengeType.SpecificSuit => cardList.Any(c => (int)c.Suit == wager.ChallengeValue),
                _ => false
            };

            if (isCompleted)
            {
                results.Add(new WagerResult
                {
                    Wager = wager,
                    IsCompleted = true,
                    ChipsAwarded = wager.RewardChips
                });
            }
        }

        return results;
    }
}

public class WagerResult
{
    public required ItemWager Wager { get; init; }
    public bool IsCompleted { get; init; }
    public int ChipsAwarded { get; init; }
}
