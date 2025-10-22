namespace ChefKnifeStudios.PokerAttack.Server.Core.Models;

public class ActiveGame
{
    public required string Id { get; set; }
    public HashSet<Player> Players { get; set; } = new();
}