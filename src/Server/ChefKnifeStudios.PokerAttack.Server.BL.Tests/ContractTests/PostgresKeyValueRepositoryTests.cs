using ChefKnifeStudios.PokerAttack.Server.Core.Models;
using ChefKnifeStudios.PokerAttack.Server.Data;
using ChefKnifeStudios.PokerAttack.Server.Infrastructure;
using ChefKnifeStudios.PokerAttack.Shared;
using ChefKnifeStudios.PokerAttack.Shared.Enums;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;

namespace ChefKnifeStudios.PokerAttack.Server.Tests.ContractTests;

// Tier 2: crosses a real boundary (Npgsql + JSONB polymorphic serialization). A stub would
// test the stub — this proves round-tripping through a real Postgres instance. Requires
// Docker; each run gets a fresh container/schema, no suite-wide mutable fixture.
[TestFixture]
public class PostgresKeyValueRepositoryTests
{
    PostgreSqlContainer _container = null!;
    AppDbContext _dbContext = null!;

    [OneTimeSetUp]
    public async Task OneTimeSetup()
    {
        _container = new PostgreSqlBuilder()
            .WithImage("postgres:16-alpine")
            .Build();
        await _container.StartAsync();
    }

    [OneTimeTearDown]
    public async Task OneTimeTeardown()
    {
        await _container.DisposeAsync();
    }

    [SetUp]
    public async Task Setup()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(_container.GetConnectionString())
            .Options;
        _dbContext = new AppDbContext(options);
        await _dbContext.Database.MigrateAsync();
    }

    [TearDown]
    public void Teardown() => _dbContext.Dispose();

    [Test]
    public async Task ActiveGame_RoundTrips_ThroughPostgres()
    {
        var repo = new PostgresKeyValueRepository<ActiveGame>(_dbContext);
        var game = new ActiveGame
        {
            Id = "game-1",
            Players = new HashSet<Player> { new() { Id = "p1", Name = "Alice" } },
            RoundNumber = 3,
            CurrentPhase = GameStates.InGame,
            PhaseStartedAt = DateTime.UtcNow,
            PhaseExpiresAt = DateTime.UtcNow.AddSeconds(30),
        };

        await repo.AddAsync(game.Id, game);
        var fetched = (await repo.GetAsync(game.Id)).Value;

        Assert.That(fetched, Is.Not.Null);
        Assert.That(fetched!.RoundNumber, Is.EqualTo(3));
        Assert.That(fetched.CurrentPhase, Is.EqualTo(GameStates.InGame));
        Assert.That(fetched.Players.Single().Id, Is.EqualTo("p1"));
    }

    [Test]
    public async Task GamePlayer_WithPolymorphicItemsAndDeckAndCards_RoundTrips()
    {
        var repo = new PostgresKeyValueRepository<GamePlayer>(_dbContext);
        var deck = new Deck();
        deck.RandomizeDeck();

        var gamePlayer = new GamePlayer
        {
            Deck = deck,
            Score = 42,
            PurchasedItems =
            [
                new ItemWager
                {
                    Id = "w1",
                    Name = "Wager",
                    Description = "desc",
                    Price = 10,
                    ChallengeType = WagerChallengeType.Score,
                    ChallengeValue = 100,
                    RewardChips = 5,
                    IsCompleted = true,
                },
            ],
        };
        gamePlayer.CardsInHand.Add(deck.PullCard());

        await repo.AddAsync("player-1", gamePlayer);
        var fetched = (await repo.GetAsync("player-1")).Value;

        Assert.That(fetched, Is.Not.Null);
        Assert.That(fetched!.Score, Is.EqualTo(42));
        Assert.That(fetched.CardsInHand, Has.Count.EqualTo(1));
        Assert.That(fetched.PurchasedItems.Single(), Is.InstanceOf<ItemWager>());
        Assert.That(((ItemWager)fetched.PurchasedItems.Single()).IsCompleted, Is.True);
    }

    [Test]
    public async Task Lobby_RoundTrips()
    {
        var repo = new PostgresKeyValueRepository<Lobby>(_dbContext);
        var host = new Player { Id = "host-1", Name = "Host" };
        var lobby = new Lobby { Id = "lobby-1", HostPlayer = host, Players = [host], CreatedAt = DateTime.UtcNow };

        await repo.AddAsync(lobby.Id, lobby);
        var fetched = (await repo.GetAsync(lobby.Id)).Value;

        Assert.That(fetched, Is.Not.Null);
        Assert.That(fetched!.HostPlayer.Id, Is.EqualTo("host-1"));
    }

    [Test]
    public async Task GameStates_And_GameModes_RoundTrip()
    {
        var stateRepo = new PostgresKeyValueRepository<GameStates>(_dbContext);
        var modeRepo = new PostgresKeyValueRepository<GameModes>(_dbContext);

        await stateRepo.AddAsync("game-1", GameStates.Shop);
        await modeRepo.AddAsync("game-1", GameModes.Full);

        Assert.That((await stateRepo.GetAsync("game-1")).Value, Is.EqualTo(GameStates.Shop));
        Assert.That((await modeRepo.GetAsync("game-1")).Value, Is.EqualTo(GameModes.Full));
    }

    [Test]
    public async Task ActiveGameAndGameStates_SameKey_DoNotCollide_AcrossTypes()
    {
        // ActiveGame and GameStates are both keyed by game id — the (type, key) composite
        // primary key is what stops them from colliding in a single shared table.
        var activeGameRepo = new PostgresKeyValueRepository<ActiveGame>(_dbContext);
        var stateRepo = new PostgresKeyValueRepository<GameStates>(_dbContext);

        await activeGameRepo.AddAsync("shared-id", new ActiveGame { Id = "shared-id" });
        await stateRepo.AddAsync("shared-id", GameStates.Freebie);

        Assert.That((await activeGameRepo.GetAsync("shared-id")).Value, Is.Not.Null);
        Assert.That((await stateRepo.GetAsync("shared-id")).Value, Is.EqualTo(GameStates.Freebie));
    }

    [Test]
    public async Task ExpiredStateSweeper_RemovesOnlyPastExpiry()
    {
        var repo = new PostgresKeyValueRepository<Lobby>(_dbContext);
        var host = new Player { Id = "h", Name = "Host" };

        await repo.AddAsync("expired-lobby", new Lobby { Id = "expired-lobby", HostPlayer = host, Players = [host], CreatedAt = DateTime.UtcNow }, DateTime.UtcNow.AddSeconds(-1));
        await repo.AddAsync("live-lobby", new Lobby { Id = "live-lobby", HostPlayer = host, Players = [host], CreatedAt = DateTime.UtcNow }, DateTime.UtcNow.AddHours(24));

        var sweeper = new ExpiredStateSweeper(_dbContext);
        var removed = await sweeper.SweepExpiredAsync(DateTime.UtcNow);

        Assert.That(removed.Select(r => r.Key), Does.Contain("expired-lobby"));
        Assert.That(removed.Select(r => r.Key), Does.Not.Contain("live-lobby"));
        Assert.That((await repo.GetAsync("live-lobby")).Value, Is.Not.Null);
    }
}
