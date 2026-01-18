using ChefKnifeStudios.PokerAttack.Shared;
using ChefKnifeStudios.PokerAttack.Shared.Enums;

namespace ChefKnifeStudios.PokerAttack.Server.Core.Models;

public class SoloGame
{
    public required string Id { get; set; }
    public required string PlayerId { get; set; }
    public required string PlayerName { get; set; }
    public int RoundNumber { get; set; } = 1;
    public SoloGamePhase Phase { get; set; } = SoloGamePhase.InGame;
    public required Deck Deck { get; set; }
    public List<Card> CardsInHand { get; set; } = [];
    public int Score { get; set; } = 0;
    public int Wallet { get; set; } = 0;
    public List<ItemBase> PurchasedItems { get; set; } = [];
    public IEnumerable<ItemBase> ActiveItems => PurchasedItems.Where(item => item is not ItemWager wager || !wager.IsCompleted);
    public IEnumerable<ItemWager> ActiveWagers => PurchasedItems.OfType<ItemWager>().Where(w => !w.IsCompleted);
    public int HandsRemaining { get; set; }
    public int DiscardsRemaining { get; set; }

    // Threshold config
    public int BaseThreshold { get; set; } = 500;
    public int ThresholdIncrement { get; set; } = 250;

    public int GetCurrentThreshold() => BaseThreshold + (ThresholdIncrement * (RoundNumber - 1));
}
