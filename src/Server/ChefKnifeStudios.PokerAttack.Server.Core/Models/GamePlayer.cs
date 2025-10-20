using ChefKnifeStudios.PokerAttack.Shared.Enums;

namespace ChefKnifeStudios.PokerAttack.Server.Core.Models;

public class GamePlayer
{
    public required Deck Deck { get; set; }
    public List<Card> CardsInHand { get; set; } = [];
    public int Score { get; set; }
    public int PowerPoints { get; set; }
    public PlayerPower? PlayerPower { get; set; }

    public void UnflipAllCards()
    {
        foreach (var card in CardsInHand)
        {
            card.IsFaceDown = false;
        }
    }

    public void FlipRandomCards(int count)
    {
        var nonFlippedCards = CardsInHand
            .Where(x => !x.IsFaceDown)
            .ToList();
        nonFlippedCards = GetShuffleCards(nonFlippedCards);
        if (nonFlippedCards.Count() < count)
        {
            foreach (var card in nonFlippedCards)
            {
                card.IsFaceDown = true;
            }
        }
        else
        {
            for (int i = 0; i < count; i += 1)
            {
                nonFlippedCards[i].IsFaceDown = true;
            }
        }
    }

    public void ChangeRandomCardsToRandomSuit(int count)
    {
        var shuffledCards = GetShuffleCards(CardsInHand);
        if (shuffledCards.Count() < count)
        {
            foreach (var card in shuffledCards)
            {
                card.Suit = (Suits)Random.Shared.Next(0, 4);
            }
        }
        else
        {
            for (int i = 0; i < count; i += 1)
            {
                shuffledCards[i].Suit = (Suits)Random.Shared.Next(0, 4);
            }
        }
    }

    public void ChangeRandomCardsToRandomRank(int count)
    {
        var shuffledCards = GetShuffleCards(CardsInHand);
        if (shuffledCards.Count() < count)
        {
            foreach (var card in shuffledCards)
            {
                card.Rank = (Ranks)Random.Shared.Next(0, 13);
            }
        }
        else
        {
            for (int i = 0; i < count; i += 1)
            {
                shuffledCards[i].Suit = (Suits)Random.Shared.Next(0, 4);
            }
        }
    }

    public void ChangeSelectedCardsToSuit(Suits suit, IEnumerable<Card> selectedCards)
    {
        // Group selected cards by Rank and Suit so duplicates are handled correctly
        var selectedGroups = selectedCards
            .GroupBy(c => new { c.Rank, c.Suit })
            .ToDictionary(g => g.Key, g => g.Count());

        foreach (var group in selectedGroups)
        {
            // Find matching cards in hand (same Suit and Rank)
            var matchingCards = CardsInHand
                .Where(c => c.Rank == group.Key.Rank && c.Suit == group.Key.Suit)
                .Take(group.Value); // Only take as many as selected

            // Change their suit
            foreach (var card in matchingCards)
            {
                card.Suit = suit;
            }
        }
    }

    List<Card> GetShuffleCards(List<Card> cardList)
    {
        var result = cardList.ToList();
        int n = result.Count;
        if (n <= 1) return result;

        for (int i = n - 1; i > 0; i--)
        {
            int j = Random.Shared.Next(i + 1); // 0..i
            var tmp = result[i];
            result[i] = result[j];
            result[j] = tmp;
        }
        return result;
    }
}
