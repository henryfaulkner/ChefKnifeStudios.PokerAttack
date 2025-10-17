using ChefKnifeStudios.PokerAttack.Shared.DTOs.Gameplay;

namespace ChefKnifeStudios.PokerAttack.Server.Core.Models;

public class GamePlayer
{
    public required Deck Deck { get; set; }
    public List<Card> CardsInHand { get; set; } = [];
    public int Score { get; set; }
    public int PowerPoints { get; set; }
}
