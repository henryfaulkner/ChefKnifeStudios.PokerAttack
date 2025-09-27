using ChefKnifeStudios.PokerAttack.Shared.DTOs.Gameplay;
using ChefKnifeStudios.PokerAttack.Shared.Enums;

namespace ChefKnifeStudios.PokerAttack.Client.Shared.Models;

public class CardItem
{
    public CardItem(CardDTO card)
    {
        Suit = card.Suit;
        Rank = card.Rank;
        IsSelected = false;
    }

    public Suits Suit { get; init; }
    public Ranks Rank { get; init; }
    public bool IsSelected { get; set; }
}
