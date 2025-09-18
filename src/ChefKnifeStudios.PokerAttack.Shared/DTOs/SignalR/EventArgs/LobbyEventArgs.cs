using ChefKnifeStudios.PokerAttack.Shared.DTOs.Lobby;

namespace ChefKnifeStudios.PokerAttack.Shared.DTOs.SignalR.EventArgs;

public class LobbyEventArgs
{
    public required LobbyDTO Lobby { get; init; }
}
