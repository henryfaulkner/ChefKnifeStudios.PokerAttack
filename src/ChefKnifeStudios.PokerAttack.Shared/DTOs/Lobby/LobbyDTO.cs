namespace ChefKnifeStudios.PokerAttack.Shared.DTOs.Lobby;

public class LobbyDTO
{
    public required string GameId { get; set; }
    public required PlayerDTO HostPlayer { get; set; }
    public HashSet<PlayerDTO> Players { get; set; } = new();
    public bool InProgress { get; set; }
}