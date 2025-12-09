namespace ChefKnifeStudios.PokerAttack.Server.Core.Models;

public class Lobby
{
    public required string Id { get; set; }
    public required Player HostPlayer { get; set; }
    public HashSet<Player> Players { get; set; } = new();
    public string? GameId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}