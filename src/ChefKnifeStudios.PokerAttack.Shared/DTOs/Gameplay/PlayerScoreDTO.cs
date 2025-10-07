using ChefKnifeStudios.PokerAttack.Shared.DTOs.Lobby;

namespace ChefKnifeStudios.PokerAttack.Shared.DTOs.Gameplay;

public class PlayerScoreDTO
{
    public required PlayerDTO Player { get; set; }
    public int Score { get; set; }
}
