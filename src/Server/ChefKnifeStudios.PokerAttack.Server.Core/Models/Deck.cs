using ChefKnifeStudios.PokerAttack.Server.Core.Interfaces;
using ChefKnifeStudios.PokerAttack.Shared.Enums;

namespace ChefKnifeStudios.PokerAttack.Server.Core.Models;

public class Deck : ICardPile
{
    const int _NUM_SHIFTS = 10000;
    static readonly Random Rand = new Random();

    public Stack<Card> Cards { get; set; } = new Stack<Card>();
    public DiscardPile DiscardPile { get; set; } = new DiscardPile();

    public Deck()
    {
        PopulateDeck();
    }

    public void PopulateDeck()
    {
        foreach (var suit in Enum.GetValues<Suits>())
            foreach (var value in Enum.GetValues<Ranks>())
                Cards.Push(
                    new Card
                    {
                        Suit = suit,
                        Rank = value,
                    }
                );
    }

    public void RandomizeDeck()
    {
        var cardList = Cards.ToList();

        for (int i = 0; i < _NUM_SHIFTS; i++)
        {
            int randNumOne = Rand.Next(52);
            int randNumTwo = Rand.Next(52);
            Card tempCard = cardList[randNumOne];
            cardList[randNumOne] = cardList[randNumTwo];
            cardList[randNumTwo] = tempCard;
        }

        Cards = new Stack<Card>(cardList);
    }

    public Card PullCard()
    {
        if (Cards.Count == 0)
        {
            PopulateDeck();
            RandomizeDeck();
            DiscardPile.EmptyPile();
        }
        var card = Cards.Pop();
        DiscardPile.AddCard(card);
        return card;
    }
}
