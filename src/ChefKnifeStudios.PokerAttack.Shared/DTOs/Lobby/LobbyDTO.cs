namespace ChefKnifeStudios.PokerAttack.Shared.DTOs.Lobby;

public class LobbyDTO
{
    public required string GameId { get; set; }
    public string? HostPlayerId { get; set; }
    public HashSet<string> PlayerIds { get; set; } = new();
}