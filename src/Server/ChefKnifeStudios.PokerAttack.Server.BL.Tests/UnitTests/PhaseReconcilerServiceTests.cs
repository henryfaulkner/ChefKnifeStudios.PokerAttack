using Ardalis.Result;
using ChefKnifeStudios.PokerAttack.Server.BL;
using ChefKnifeStudios.PokerAttack.Server.BL.Services;
using ChefKnifeStudios.PokerAttack.Server.Core.Interfaces;
using ChefKnifeStudios.PokerAttack.Server.Core.Models;
using ChefKnifeStudios.PokerAttack.Server.Tests.Doubles;
using ChefKnifeStudios.PokerAttack.Shared.Enums;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;

namespace ChefKnifeStudios.PokerAttack.Server.Tests.UnitTests;

[TestFixture]
public class PhaseReconcilerServiceTests
{
    FakeClock _clock = null!;
    Mock<IKeyValueRepository<ActiveGame>> _mockActiveGameRepo = null!;
    Mock<IGameService> _mockGameService = null!;
    Mock<IPlayerSessionStore> _mockSessionStore = null!;
    ServiceProvider _serviceProvider = null!;
    PhaseReconcilerService _reconciler = null!;

    const string GameId = "game-1";

    [SetUp]
    public void Setup()
    {
        _clock = new FakeClock(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        _mockActiveGameRepo = new Mock<IKeyValueRepository<ActiveGame>>();
        _mockGameService = new Mock<IGameService>();
        _mockSessionStore = new Mock<IPlayerSessionStore>();

        _mockSessionStore
            .Setup(s => s.FindOverdueByDisconnectedAtAsync(It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<IReadOnlyList<PlayerSession>>([]));

        var services = new ServiceCollection();
        services.AddSingleton(_mockActiveGameRepo.Object);
        services.AddSingleton(_mockGameService.Object);
        services.AddSingleton(_mockSessionStore.Object);
        services.AddSingleton(Mock.Of<ILobbyService>());
        _serviceProvider = services.BuildServiceProvider();

        _reconciler = new PhaseReconcilerService(
            _serviceProvider.GetRequiredService<IServiceScopeFactory>(),
            _clock,
            Options.Create(new ResilienceSettings()),
            Mock.Of<ILogger<PhaseReconcilerService>>());
    }

    [TearDown]
    public void TearDown() => _serviceProvider.Dispose();

    ActiveGame MakeGame(GameStates phase, DateTime? expiresAt) => new()
    {
        Id = GameId,
        CurrentPhase = phase,
        PhaseStartedAt = _clock.UtcNow,
        PhaseExpiresAt = expiresAt,
    };

    [Test]
    public async Task ReconcileGameAsync_AdvancesOverduePhase_ExactlyOnce()
    {
        var game = MakeGame(GameStates.InGame, _clock.UtcNow.AddSeconds(-1));
        _mockActiveGameRepo.Setup(r => r.GetAsync(GameId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<ActiveGame?>(game));
        _mockActiveGameRepo.Setup(r => r.UpdateAsync(GameId, It.IsAny<ActiveGame>(), It.IsAny<CancellationToken>()))
            .Callback<string, ActiveGame, CancellationToken>((_, updated, _) => game = updated)
            .ReturnsAsync(Result.Success());
        _mockGameService.Setup(s => s.EndRoundAsync(GameId, It.IsAny<CancellationToken>()))
            .Callback(() => game.PhaseExpiresAt = null)
            .ReturnsAsync(Result.Success());

        await _reconciler.ReconcileGameAsync(GameId);

        _mockGameService.Verify(s => s.EndRoundAsync(GameId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task ReconcileGameAsync_NoOps_WhenNothingOverdue()
    {
        var game = MakeGame(GameStates.InGame, _clock.UtcNow.AddSeconds(30));
        _mockActiveGameRepo.Setup(r => r.GetAsync(GameId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<ActiveGame?>(game));

        await _reconciler.ReconcileGameAsync(GameId);

        _mockGameService.Verify(s => s.EndRoundAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        _mockActiveGameRepo.Verify(r => r.UpdateAsync(It.IsAny<string>(), It.IsAny<ActiveGame>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Test]
    public async Task ReconcileGameAsync_AdvancesThroughMultipleExpiredDeadlines_DuringOneInterruption()
    {
        // Simulates round -> scoreboard both expiring while the process was down: each Finish*
        // call sets the ActiveGame into the next phase with its own (now also overdue) deadline.
        var game = MakeGame(GameStates.InGame, _clock.UtcNow.AddSeconds(-20));

        _mockActiveGameRepo.Setup(r => r.GetAsync(GameId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => Result.Success<ActiveGame?>(game));
        _mockActiveGameRepo.Setup(r => r.UpdateAsync(GameId, It.IsAny<ActiveGame>(), It.IsAny<CancellationToken>()))
            .Callback<string, ActiveGame, CancellationToken>((_, updated, _) => game = updated)
            .ReturnsAsync(Result.Success());

        _mockGameService.Setup(s => s.EndRoundAsync(GameId, It.IsAny<CancellationToken>()))
            .Callback(() =>
            {
                game.CurrentPhase = GameStates.Scoreboard;
                game.PhaseExpiresAt = _clock.UtcNow.AddSeconds(-5); // already overdue too
            })
            .ReturnsAsync(Result.Success());

        _mockGameService.Setup(s => s.FinishScoreboardAsync(GameId, It.IsAny<CancellationToken>()))
            .Callback(() =>
            {
                game.CurrentPhase = GameStates.Elimination;
                game.PhaseExpiresAt = null; // resolves to current phase, not overdue anymore
            })
            .ReturnsAsync(Result.Success());

        await _reconciler.ReconcileGameAsync(GameId);

        _mockGameService.Verify(s => s.EndRoundAsync(GameId, It.IsAny<CancellationToken>()), Times.Once);
        _mockGameService.Verify(s => s.FinishScoreboardAsync(GameId, It.IsAny<CancellationToken>()), Times.Once);
        Assert.That(game.CurrentPhase, Is.EqualTo(GameStates.Elimination));
    }
}
