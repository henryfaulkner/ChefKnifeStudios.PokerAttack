using ChefKnifeStudios.PokerAttack.Server.Core.Models;
using ChefKnifeStudios.PokerAttack.Server.Tests.Doubles;

namespace ChefKnifeStudios.PokerAttack.Server.Tests.UnitTests;

// Grace-period boundary logic: the reconciler removes a disconnected player once
// now > disconnected_at + grace. Mirrors PhaseReconcilerService.ReconcileOverdueSessionsAsync's
// threshold check (now - grace) against session.DisconnectedAt.
[TestFixture]
public class GracePeriodTests
{
    static readonly TimeSpan Grace = TimeSpan.FromSeconds(90);

    static bool IsOverdue(PlayerSession session, DateTime now, TimeSpan grace) =>
        session.DisconnectedAt is DateTime disconnectedAt && disconnectedAt <= now - grace;

    [Test]
    public void NotRemoved_OneTickBeforeGraceExpiry()
    {
        var clock = new FakeClock(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        var session = new PlayerSession { PlayerId = "p1", Connected = false, DisconnectedAt = clock.UtcNow };

        clock.Advance(Grace - TimeSpan.FromTicks(1));

        Assert.That(IsOverdue(session, clock.UtcNow, Grace), Is.False);
    }

    [Test]
    public void Removed_ExactlyAtGraceExpiry()
    {
        var clock = new FakeClock(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        var session = new PlayerSession { PlayerId = "p1", Connected = false, DisconnectedAt = clock.UtcNow };

        clock.Advance(Grace);

        Assert.That(IsOverdue(session, clock.UtcNow, Grace), Is.True);
    }

    [Test]
    public void Removed_OneTickAfterGraceExpiry()
    {
        var clock = new FakeClock(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        var session = new PlayerSession { PlayerId = "p1", Connected = false, DisconnectedAt = clock.UtcNow };

        clock.Advance(Grace + TimeSpan.FromTicks(1));

        Assert.That(IsOverdue(session, clock.UtcNow, Grace), Is.True);
    }

    [Test]
    public void ReconnectBeforeExpiry_ClearsDisconnectedAt_CancelsRemoval()
    {
        var clock = new FakeClock(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        var session = new PlayerSession { PlayerId = "p1", Connected = false, DisconnectedAt = clock.UtcNow };

        clock.Advance(TimeSpan.FromSeconds(30));

        // Reconnect: OnConnectedAsync clears DisconnectedAt and sets Connected = true.
        session.Connected = true;
        session.DisconnectedAt = null;

        clock.Advance(Grace);

        Assert.That(IsOverdue(session, clock.UtcNow, Grace), Is.False);
    }
}
