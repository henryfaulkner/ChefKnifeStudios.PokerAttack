namespace ChefKnifeStudios.PokerAttack.Server.Core.Models;

public class Lobby
{
    public string? HostPlayerId { get; set; }
    public HashSet<string> PlayerIds { get; set; } = new();
}