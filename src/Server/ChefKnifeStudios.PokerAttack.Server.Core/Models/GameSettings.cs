namespace ChefKnifeStudios.PokerAttack.Server.Core.Models;

public class GameSettings
{
    public int RoundTimeMs { get; set; } = 30000;
    public int ShopTimeMs { get; set; } = 5000;
    public int EliminationTimeMs { get; set; } = 5000;
    public int PlayerPowerSelectionTimeMs { get; set; } = 15000;
}
