namespace ChefKnifeStudios.PokerAttack.Server.Core.Models;

public class ResilienceSettings
{
    public int GracePeriodSeconds { get; set; } = 90;
    public int ReconcilerIntervalSeconds { get; set; } = 30;
}
