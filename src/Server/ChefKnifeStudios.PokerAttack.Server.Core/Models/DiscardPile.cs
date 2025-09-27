using ChefKnifeStudios.PokerAttack.Server.Core.Interfaces;

namespace ChefKnifeStudios.PokerAttack.Server.Core.Models;

public class DiscardPile : ICardPile
{
    Stack<Card> _cardPile { get; set; } = new Stack<Card>();

    public void AddCard(Card card) => _cardPile.Push(card);
    public void EmptyPile() => _cardPile.Clear();
}
