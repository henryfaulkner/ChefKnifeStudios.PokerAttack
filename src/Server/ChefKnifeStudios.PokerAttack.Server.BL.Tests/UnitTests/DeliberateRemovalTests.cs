using Ardalis.Result;
using ChefKnifeStudios.PokerAttack.Server.BL.Services;
using ChefKnifeStudios.PokerAttack.Server.Core.Interfaces;
using ChefKnifeStudios.PokerAttack.Server.Core.Models;
using ChefKnifeStudios.PokerAttack.Server.Data.Repos;
using ChefKnifeStudios.PokerAttack.Shared;
using ChefKnifeStudios.PokerAttack.Shared.Enums;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using DataGame = ChefKnifeStudios.PokerAttack.Server.Data.Models.Game;
using DataRound = ChefKnifeStudios.PokerAttack.Server.Data.Models.Round;

namespace ChefKnifeStudios.PokerAttack.Server.Tests.UnitTests;

// FinishEliminationAsync removes losers via LeaveGameAsync directly, which never touches
// IPlayerSessionStore or the grace period — that path is disconnect-only, in the hub. This
// guards the regression risk flagged in the plan: an eliminated player must never linger.
[TestFixture]
public class DeliberateRemovalTests
{
    Mock<IKeyValueRepository<ActiveGame>> _mockActiveGameRepo = null!;
    Mock<IKeyValueRepository<GamePlayer>> _mockGamePlayerRepo = null!;
    Mock<IPokerAttackNotificationHelper> _mockNotificationHelper = null!;
    GameService _service = null!;

    const string GameId = "game-1";
    const string LoserId = "loser-1";
    const string WinnerId = "winner-1";

    [SetUp]
    public void Setup()
    {
        _mockActiveGameRepo = new Mock<IKeyValueRepository<ActiveGame>>();
        _mockGamePlayerRepo = new Mock<IKeyValueRepository<GamePlayer>>();
        _mockNotificationHelper = new Mock<IPokerAttackNotificationHelper>();

        var activeGame = new ActiveGame
        {
            Id = GameId,
            Players = new HashSet<Player>
            {
                new() { Id = LoserId, Name = "Loser" },
                new() { Id = WinnerId, Name = "Winner" },
            },
        };

        _mockActiveGameRepo.Setup(r => r.GetAsync(GameId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<ActiveGame?>(activeGame));
        _mockActiveGameRepo.Setup(r => r.UpdateAsync(GameId, It.IsAny<ActiveGame>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success());

        var loserState = new GamePlayer { Deck = new Deck(), Score = 0, IsEliminating = true };
        var winnerState = new GamePlayer { Deck = new Deck(), Score = 100, IsEliminating = false };

        _mockGamePlayerRepo.Setup(r => r.GetAsync(LoserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<GamePlayer?>(loserState));
        _mockGamePlayerRepo.Setup(r => r.GetAsync(WinnerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<GamePlayer?>(winnerState));
        _mockGamePlayerRepo.Setup(r => r.UpdateAsync(It.IsAny<string>(), It.IsAny<GamePlayer>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success());
        _mockGamePlayerRepo.Setup(r => r.DeleteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success());

        var mockGameStateMachineService = new Mock<IGameStateMachineService>();
        mockGameStateMachineService
            .Setup(s => s.TransitionAsync(It.IsAny<string>(), It.IsAny<GameEvents>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success());

        _service = new GameService(
            Mock.Of<ILogger<GameService>>(),
            _mockActiveGameRepo.Object,
            _mockGamePlayerRepo.Object,
            Mock.Of<IKeyValueRepository<GameStates>>(),
            Mock.Of<IKeyValueRepository<Lobby>>(),
            Mock.Of<IRepository<DataGame>>(),
            Mock.Of<IRepository<DataRound>>(),
            _mockNotificationHelper.Object,
            mockGameStateMachineService.Object,
            Mock.Of<IScoringRulesService>(),
            Mock.Of<IItemEffectsService>(),
            Mock.Of<IWagerService>(),
            Mock.Of<IPlayerPowerService>(),
            Options.Create(new GameSettings()),
            new ChefKnifeStudios.PokerAttack.Server.Tests.Doubles.FakeClock(DateTime.UtcNow)
        );
    }

    [Test]
    public async Task FinishEliminationAsync_RemovesLoser_Immediately_NotViaGracePeriod()
    {
        var result = await _service.FinishEliminationAsync(GameId);

        Assert.That(result.IsSuccess, Is.True);

        // The loser is removed from the active game's player set synchronously within this call —
        // no disconnect, no grace period, no IPlayerSessionStore involved.
        _mockActiveGameRepo.Verify(
            r => r.UpdateAsync(GameId, It.Is<ActiveGame>(g => !g.Players.Any(p => p.Id == LoserId)), It.IsAny<CancellationToken>()),
            Times.Once);
        _mockGamePlayerRepo.Verify(r => r.DeleteAsync(LoserId, It.IsAny<CancellationToken>()), Times.Once);

        // The winner is untouched by the removal path.
        _mockGamePlayerRepo.Verify(r => r.DeleteAsync(WinnerId, It.IsAny<CancellationToken>()), Times.Never);
    }

    [Test]
    public async Task LeaveGameAsync_RemovesPlayer_Immediately_NoGracePeriodDependency()
    {
        var result = await _service.LeaveGameAsync(GameId, LoserId);

        Assert.That(result.IsSuccess, Is.True);
        _mockGamePlayerRepo.Verify(r => r.DeleteAsync(LoserId, It.IsAny<CancellationToken>()), Times.Once);
        _mockNotificationHelper.Verify(h => h.LeaveGameGroupForUserAsync(LoserId, GameId, It.IsAny<CancellationToken>()), Times.Once);
    }
}
