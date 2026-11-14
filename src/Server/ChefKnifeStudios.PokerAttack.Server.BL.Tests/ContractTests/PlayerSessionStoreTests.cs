using ChefKnifeStudios.PokerAttack.Server.Core.Models;
using ChefKnifeStudios.PokerAttack.Server.Data;
using ChefKnifeStudios.PokerAttack.Server.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;

namespace ChefKnifeStudios.PokerAttack.Server.Tests.ContractTests;

// Proves the indexed player_id -> game_id lookup returns the same result the previous
// linear scan did, over a real Postgres instance.
[TestFixture]
public class PlayerSessionStoreTests
{
    PostgreSqlContainer _container = null!;
    AppDbContext _dbContext = null!;
    PlayerSessionStore _store = null!;

    [OneTimeSetUp]
    public async Task OneTimeSetup()
    {
        _container = new PostgreSqlBuilder().WithImage("postgres:16-alpine").Build();
        await _container.StartAsync();
    }

    [OneTimeTearDown]
    public async Task OneTimeTeardown() => await _container.DisposeAsync();

    [SetUp]
    public async Task Setup()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(_container.GetConnectionString())
            .Options;
        _dbContext = new AppDbContext(options);
        await _dbContext.Database.MigrateAsync();
        _store = new PlayerSessionStore(_dbContext);
    }

    [TearDown]
    public void Teardown() => _dbContext.Dispose();

    [Test]
    public async Task FindByGameAsync_ReturnsAllSessionsForThatGame_NotOthers()
    {
        await _store.UpsertAsync(new PlayerSession { PlayerId = "p1", GameId = "game-a", Connected = true, UpdatedAt = DateTime.UtcNow });
        await _store.UpsertAsync(new PlayerSession { PlayerId = "p2", GameId = "game-a", Connected = true, UpdatedAt = DateTime.UtcNow });
        await _store.UpsertAsync(new PlayerSession { PlayerId = "p3", GameId = "game-b", Connected = true, UpdatedAt = DateTime.UtcNow });

        var result = await _store.FindByGameAsync("game-a");

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value.Select(s => s.PlayerId), Is.EquivalentTo(new[] { "p1", "p2" }));
    }

    [Test]
    public async Task GetByPlayerAsync_ReturnsNull_WhenNoSessionExists()
    {
        var result = await _store.GetByPlayerAsync("unknown-player");

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value, Is.Null);
    }

    [Test]
    public async Task UpsertAsync_UpdatesExistingSession_RatherThanDuplicating()
    {
        await _store.UpsertAsync(new PlayerSession { PlayerId = "p1", GameId = "game-a", Connected = true, UpdatedAt = DateTime.UtcNow });
        await _store.UpsertAsync(new PlayerSession { PlayerId = "p1", GameId = "game-b", Connected = false, DisconnectedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow });

        var result = await _store.GetByPlayerAsync("p1");

        Assert.That(result.Value!.GameId, Is.EqualTo("game-b"));
        Assert.That(result.Value.Connected, Is.False);
    }

    [Test]
    public async Task FindOverdueByDisconnectedAtAsync_ReturnsOnlySessionsPastThreshold()
    {
        var now = DateTime.UtcNow;
        await _store.UpsertAsync(new PlayerSession { PlayerId = "overdue", Connected = false, DisconnectedAt = now.AddMinutes(-5), UpdatedAt = now });
        await _store.UpsertAsync(new PlayerSession { PlayerId = "recent", Connected = false, DisconnectedAt = now.AddSeconds(-5), UpdatedAt = now });
        await _store.UpsertAsync(new PlayerSession { PlayerId = "connected", Connected = true, DisconnectedAt = null, UpdatedAt = now });

        var result = await _store.FindOverdueByDisconnectedAtAsync(now.AddMinutes(-1));

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value.Select(s => s.PlayerId), Is.EquivalentTo(new[] { "overdue" }));
    }
}
