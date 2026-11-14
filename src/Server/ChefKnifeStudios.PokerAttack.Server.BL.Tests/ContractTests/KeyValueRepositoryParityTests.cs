using ChefKnifeStudios.PokerAttack.Server.Core.Interfaces;
using ChefKnifeStudios.PokerAttack.Server.Core.Models;
using ChefKnifeStudios.PokerAttack.Server.Data;
using ChefKnifeStudios.PokerAttack.Server.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;

namespace ChefKnifeStudios.PokerAttack.Server.Tests.ContractTests;

// Protects the DI swap at Program.cs (InMemoryKeyValueRepository<> <-> PostgresKeyValueRepository<>),
// which is the whole premise of externalizing state: one shared test body run against both
// implementations proves behavioural parity rather than asserting each independently.
[TestFixture]
public class KeyValueRepositoryParityTests
{
    PostgreSqlContainer _container = null!;
    AppDbContext _dbContext = null!;

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
    }

    [TearDown]
    public void Teardown() => _dbContext.Dispose();

    static async Task RunSharedBehaviour(IKeyValueRepository<Lobby> repo)
    {
        var host = new Player { Id = "host-1", Name = "Host" };
        var lobby = new Lobby { Id = "lobby-1", HostPlayer = host, Players = [host], CreatedAt = DateTime.UtcNow };

        // Add then get
        var addResult = await repo.AddAsync(lobby.Id, lobby);
        Assert.That(addResult.IsSuccess, Is.True);
        Assert.That((await repo.GetAsync(lobby.Id)).Value?.Id, Is.EqualTo(lobby.Id));

        // Duplicate add conflicts
        var conflictResult = await repo.AddAsync(lobby.Id, lobby);
        Assert.That(conflictResult.IsSuccess, Is.False);

        // Update
        lobby.GameId = "game-99";
        var updateResult = await repo.UpdateAsync(lobby.Id, lobby);
        Assert.That(updateResult.IsSuccess, Is.True);
        Assert.That((await repo.GetAsync(lobby.Id)).Value?.GameId, Is.EqualTo("game-99"));

        // Update on missing key fails
        var missingUpdateResult = await repo.UpdateAsync("missing-id", lobby);
        Assert.That(missingUpdateResult.IsSuccess, Is.False);

        // GetAll includes what was added
        var allResult = await repo.GetAllAsync();
        Assert.That(allResult.Value.Keys, Does.Contain(lobby.Id));

        // Delete then get returns null
        var deleteResult = await repo.DeleteAsync(lobby.Id);
        Assert.That(deleteResult.IsSuccess, Is.True);
        Assert.That((await repo.GetAsync(lobby.Id)).Value, Is.Null);
    }

    [Test]
    public async Task InMemoryRepository_MatchesSharedBehaviour()
    {
        await RunSharedBehaviour(new InMemoryKeyValueRepository<Lobby>());
    }

    [Test]
    public async Task PostgresRepository_MatchesSharedBehaviour()
    {
        await RunSharedBehaviour(new PostgresKeyValueRepository<Lobby>(_dbContext));
    }
}
