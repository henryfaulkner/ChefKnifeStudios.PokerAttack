using ChefKnifeStudios.PokerAttack.Shared.Enums;

namespace ChefKnifeStudios.PokerAttack.Server.Core.Models;

public class Card
{
    public Suits Suit { get; set; }
    public Ranks Rank { get; set; }
    public bool IsFaceDown { get; set; } = false;
}

