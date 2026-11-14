using ChefKnifeStudios.PokerAttack.Server.Core.Interfaces;

namespace ChefKnifeStudios.PokerAttack.Server.Tests.Doubles;

public class FakeClock(DateTime initial) : IClock
{
    public DateTime UtcNow { get; private set; } = initial;

    public void Advance(TimeSpan duration) => UtcNow = UtcNow.Add(duration);
}
