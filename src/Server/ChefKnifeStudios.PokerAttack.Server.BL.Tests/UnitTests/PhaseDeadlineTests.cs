using ChefKnifeStudios.PokerAttack.Server.Core.Models;
using ChefKnifeStudios.PokerAttack.Server.Tests.Doubles;
using ChefKnifeStudios.PokerAttack.Shared.Enums;

namespace ChefKnifeStudios.PokerAttack.Server.Tests.UnitTests;

// Pure arithmetic over ActiveGame's phase deadline fields against a FakeClock — no I/O,
// per the plan's Testing Strategy (highest-value unit tests, no sleeping required).
[TestFixture]
public class PhaseDeadlineTests
{
    static bool IsOverdue(ActiveGame game, DateTime now) =>
        game.PhaseExpiresAt is DateTime expiresAt && expiresAt <= now;

    static TimeSpan? RemainingTime(ActiveGame game, DateTime now) =>
        game.PhaseExpiresAt is DateTime expiresAt ? expiresAt - now : null;

    [Test]
    public void IsOverdue_False_OneTickBeforeDeadline()
    {
        var clock = new FakeClock(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        var game = new ActiveGame { Id = "g1", PhaseExpiresAt = clock.UtcNow.AddSeconds(10) };

        clock.Advance(TimeSpan.FromSeconds(10) - TimeSpan.FromTicks(1));

        Assert.That(IsOverdue(game, clock.UtcNow), Is.False);
    }

    [Test]
    public void IsOverdue_True_ExactlyAtDeadline()
    {
        var clock = new FakeClock(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        var game = new ActiveGame { Id = "g1", PhaseExpiresAt = clock.UtcNow.AddSeconds(10) };

        clock.Advance(TimeSpan.FromSeconds(10));

        Assert.That(IsOverdue(game, clock.UtcNow), Is.True);
    }

    [Test]
    public void IsOverdue_True_OneTickAfterDeadline()
    {
        var clock = new FakeClock(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        var game = new ActiveGame { Id = "g1", PhaseExpiresAt = clock.UtcNow.AddSeconds(10) };

        clock.Advance(TimeSpan.FromSeconds(10) + TimeSpan.FromTicks(1));

        Assert.That(IsOverdue(game, clock.UtcNow), Is.True);
    }

    [Test]
    public void NullDeadline_MeansUntimed_NeverOverdue()
    {
        var clock = new FakeClock(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        var game = new ActiveGame { Id = "g1", CurrentPhase = GameStates.GameStart, PhaseExpiresAt = null };

        clock.Advance(TimeSpan.FromDays(365));

        Assert.That(IsOverdue(game, clock.UtcNow), Is.False);
        Assert.That(RemainingTime(game, clock.UtcNow), Is.Null);
    }

    [Test]
    public void RemainingTime_ReflectsElapsedTime()
    {
        var clock = new FakeClock(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        var game = new ActiveGame { Id = "g1", PhaseExpiresAt = clock.UtcNow.AddSeconds(30) };

        clock.Advance(TimeSpan.FromSeconds(12));

        Assert.That(RemainingTime(game, clock.UtcNow), Is.EqualTo(TimeSpan.FromSeconds(18)));
    }
}
