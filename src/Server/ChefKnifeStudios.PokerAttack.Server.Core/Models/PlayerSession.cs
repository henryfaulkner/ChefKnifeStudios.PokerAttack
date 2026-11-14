namespace ChefKnifeStudios.PokerAttack.Server.Core.Models;

public class PlayerSession
{
    public required string PlayerId { get; set; }
    public string? GameId { get; set; }
    public string? LobbyId { get; set; }
    public bool Connected { get; set; }
    public DateTime? DisconnectedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
