using ChefKnifeStudios.PokerAttack.Server.Core.Interfaces;

namespace ChefKnifeStudios.PokerAttack.Server.Infrastructure;

public class SystemClock : IClock
{
    public DateTime UtcNow => DateTime.UtcNow;
}
