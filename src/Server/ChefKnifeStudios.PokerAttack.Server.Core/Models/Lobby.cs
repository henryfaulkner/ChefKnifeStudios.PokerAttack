namespace ChefKnifeStudios.PokerAttack.Server.Core.Models;

public class Lobby
{
    public required Player HostPlayer { get; set; }
    public HashSet<Player> Players { get; set; } = new();
    public bool InProgress { get; set; }
}