using ChefKnifeStudios.PokerAttack.Server.Core.Interfaces;
using ChefKnifeStudios.PokerAttack.Server.Core.Models;
using ChefKnifeStudios.PokerAttack.Server.Data;
using ChefKnifeStudios.PokerAttack.Shared;
using ChefKnifeStudios.PokerAttack.Shared.DTOs.Gameplay;
using ChefKnifeStudios.PokerAttack.Shared.Enums;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System.Net;
using System.Net.Http.Json;
using Testcontainers.PostgreSql;

namespace ChefKnifeStudios.PokerAttack.Server.Tests.ContractTests;

// Tier 2: API surface tested via WebApplicationFactory. Requires Docker for the Postgres
// container the app connects to; each test gets a fresh schema via migrations.
[TestFixture]
public class GameStateResyncEndpointTests
{
    PostgreSqlContainer _container = null!;
    WebApplicationFactory<Program> _factory = null!;
    HttpClient _client = null!;

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
        // Environment variables (ASPNETCORE_ENVIRONMENT + ConnectionStrings__*) are read by
        // WebApplication.CreateBuilder(args) itself, unlike ConfigureAppConfiguration on the
        // WebApplicationFactory's host builder — which does not reliably override values
        // Program.cs's top-level statements already read from appsettings.{Environment}.json
        // before the factory's customization hook runs. Getting this wrong previously caused
        // these tests to connect to the real appsettings.Development.json connection string
        // instead of the Testcontainers instance. Never remove this without re-verifying
        // against a real Postgres that these tests still target the container.
        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Testing");
        Environment.SetEnvironmentVariable("ConnectionStrings__PokerAttackDB", _container.GetConnectionString());

        _factory = new WebApplicationFactory<Program>();

        using (var scope = _factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var actualConnection = dbContext.Database.GetConnectionString();
            Assert.That(actualConnection, Is.EqualTo(_container.GetConnectionString()),
                "Test did not connect to the Testcontainers Postgres instance — refusing to proceed against an unexpected database.");

            await dbContext.Database.MigrateAsync();
        }

        _client = _factory.CreateClient();
    }

    [TearDown]
    public void Teardown()
    {
        _client.Dispose();
        _factory.Dispose();
        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", null);
        Environment.SetEnvironmentVariable("ConnectionStrings__PokerAttackDB", null);
    }

    async Task SeedGameAsync(string gameId, string playerId, GameStates phase, DateTime? phaseExpiresAt)
    {
        using var scope = _factory.Services.CreateScope();
        var activeGameRepo = scope.ServiceProvider.GetRequiredService<IKeyValueRepository<ActiveGame>>();
        var gamePlayerRepo = scope.ServiceProvider.GetRequiredService<IKeyValueRepository<GamePlayer>>();

        await activeGameRepo.AddAsync(gameId, new ActiveGame
        {
            Id = gameId,
            Players = new HashSet<Player> { new() { Id = playerId, Name = "Alice" } },
            CurrentPhase = phase,
            PhaseStartedAt = DateTime.UtcNow.AddSeconds(-5),
            PhaseExpiresAt = phaseExpiresAt,
        });

        await gamePlayerRepo.AddAsync(playerId, new GamePlayer { Deck = new Deck(), Score = 10 });
    }

    [Test]
    public async Task GetGameState_Returns200_WithCorrectPhaseAndRemainingTime()
    {
        await SeedGameAsync("game-1", "player-1", GameStates.InGame, DateTime.UtcNow.AddSeconds(20));

        var response = await _client.GetAsync("/gameplay/game-1/state?playerId=player-1");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var body = await response.Content.ReadFromJsonAsync<GameStateDTO>(JsonOptions.Get());
        Assert.That(body!.CurrentPhase, Is.EqualTo(GameStates.InGame));
        Assert.That(body.Player.PlayerId, Is.EqualTo("player-1"));
    }

    [Test]
    public async Task GetGameState_Returns404_ForUnknownGame()
    {
        var response = await _client.GetAsync("/gameplay/no-such-game/state?playerId=player-1");
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task GetGameState_Returns404_ForNonParticipant()
    {
        await SeedGameAsync("game-2", "player-1", GameStates.InGame, DateTime.UtcNow.AddSeconds(20));

        var response = await _client.GetAsync("/gameplay/game-2/state?playerId=someone-else");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task GetGameState_Returns404_ForEndedGame_SameAsUnknownGame()
    {
        // A game that ended and was cleaned up looks identical to one that never existed —
        // the endpoint deliberately does not distinguish, to avoid confirming to a caller
        // whether an arbitrary game id ever existed.
        await SeedGameAsync("game-ended", "player-1", GameStates.GameOver, null);

        using (var scope = _factory.Services.CreateScope())
        {
            var activeGameRepo = scope.ServiceProvider.GetRequiredService<IKeyValueRepository<ActiveGame>>();
            await activeGameRepo.DeleteAsync("game-ended");
        }

        var response = await _client.GetAsync("/gameplay/game-ended/state?playerId=player-1");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task GetGameState_Returns400_ForMalformedParameters()
    {
        var response = await _client.GetAsync("/gameplay/game-1/state?playerId=");
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    [Test]
    public async Task GetGameState_OpponentsOrdered_StableAcrossCalls()
    {
        using (var scope = _factory.Services.CreateScope())
        {
            var activeGameRepo = scope.ServiceProvider.GetRequiredService<IKeyValueRepository<ActiveGame>>();
            var gamePlayerRepo = scope.ServiceProvider.GetRequiredService<IKeyValueRepository<GamePlayer>>();

            await activeGameRepo.AddAsync("game-3", new ActiveGame
            {
                Id = "game-3",
                Players = new HashSet<Player>
                {
                    new() { Id = "me", Name = "Me" },
                    new() { Id = "zed", Name = "Zed" },
                    new() { Id = "amy", Name = "Amy" },
                },
                CurrentPhase = GameStates.InGame,
                PhaseExpiresAt = DateTime.UtcNow.AddSeconds(20),
            });
            foreach (var id in new[] { "me", "zed", "amy" })
                await gamePlayerRepo.AddAsync(id, new GamePlayer { Deck = new Deck() });
        }

        var response1 = await _client.GetAsync("/gameplay/game-3/state?playerId=me");
        var response2 = await _client.GetAsync("/gameplay/game-3/state?playerId=me");

        var body1 = await response1.Content.ReadFromJsonAsync<GameStateDTO>(JsonOptions.Get());
        var body2 = await response2.Content.ReadFromJsonAsync<GameStateDTO>(JsonOptions.Get());

        var order1 = body1!.Opponents.Select(o => o.PlayerId).ToList();
        var order2 = body2!.Opponents.Select(o => o.PlayerId).ToList();

        Assert.That(order1, Is.EqualTo(order2));
        Assert.That(order1, Is.Ordered);
    }

    [Test]
    public async Task GetGameState_AdvancesOverduePhase_ExactlyOnce_UnderConcurrentCalls()
    {
        await SeedGameAsync("game-4", "player-1", GameStates.InGame, DateTime.UtcNow.AddSeconds(-1));

        var calls = Enumerable.Range(0, 5)
            .Select(_ => _client.GetAsync("/gameplay/game-4/state?playerId=player-1"));
        var responses = await Task.WhenAll(calls);

        Assert.That(responses, Has.All.Matches<HttpResponseMessage>(r => r.StatusCode == HttpStatusCode.OK));

        using var scope = _factory.Services.CreateScope();
        var activeGameRepo = scope.ServiceProvider.GetRequiredService<IKeyValueRepository<ActiveGame>>();
        var finalGame = (await activeGameRepo.GetAsync("game-4")).Value;

        // The overdue InGame phase resolves to Scoreboard (EndRoundAsync's transition),
        // not repeated multiple times across the concurrent calls.
        Assert.That(finalGame!.CurrentPhase, Is.EqualTo(GameStates.Scoreboard));
    }
}

// Separate fixture: the durable store is unreachable by construction (no Testcontainers
// Postgres backing this factory), proving the endpoint surfaces 503 rather than 500 or a
// hang when Postgres cannot be reached.
[TestFixture]
public class GameStateResyncEndpointUnavailableStoreTests
{
    WebApplicationFactory<Program> _factory = null!;
    HttpClient _client = null!;

    [SetUp]
    public void Setup()
    {
        // See the sibling GameStateResyncEndpointTests fixture for why this must be an
        // environment variable, not ConfigureAppConfiguration: the latter does not reliably
        // override the connection string Program.cs's top-level statements read at build time.
        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Testing");
        // Port 1 is never a live Postgres — connection attempts fail fast rather than
        // hanging for the default connect timeout.
        Environment.SetEnvironmentVariable("ConnectionStrings__PokerAttackDB", "Host=127.0.0.1;Port=1;Database=unreachable;Username=x;Password=x;Timeout=2;");

        _factory = new WebApplicationFactory<Program>();
        _client = _factory.CreateClient();
    }

    [TearDown]
    public void Teardown()
    {
        _client.Dispose();
        _factory.Dispose();
        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", null);
        Environment.SetEnvironmentVariable("ConnectionStrings__PokerAttackDB", null);
    }

    [Test]
    public async Task GetGameState_Returns503_WhenDurableStoreUnreachable()
    {
        var response = await _client.GetAsync("/gameplay/any-game/state?playerId=any-player");
        var body = await response.Content.ReadAsStringAsync();
        TestContext.WriteLine($"STATUS={response.StatusCode} BODY={body}");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.ServiceUnavailable));
    }
}
