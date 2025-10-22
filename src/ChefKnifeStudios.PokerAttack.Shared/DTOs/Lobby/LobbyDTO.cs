namespace ChefKnifeStudios.PokerAttack.Shared.DTOs.Lobby;

public class LobbyDTO
{
    public required string Id { get; set; }
    public required PlayerDTO HostPlayer { get; set; }
    public HashSet<PlayerDTO> Players { get; set; } = new();
    public string? GameId { get; set; }
}