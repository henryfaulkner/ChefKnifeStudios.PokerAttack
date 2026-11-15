using ChefKnifeStudios.PokerAttack.Server.BL.Services;
using ChefKnifeStudios.PokerAttack.Server.Core.Interfaces;
using Ardalis.Result;
using ChefKnifeStudios.PokerAttack.Server.Data.Models;
using ChefKnifeStudios.PokerAttack.Server.Data.Repos;
using ChefKnifeStudios.PokerAttack.Shared.DTOs.Lobby;
using ChefKnifeStudios.PokerAttack.Shared.Enums;
using Microsoft.Extensions.Logging.Abstractions;
using ChefKnifeStudios.PokerAttack.Server.Core.Models;
using Moq;

namespace ChefKnifeStudios.PokerAttack.Server.Tests.UnitTests;

[TestFixture]
public class LobbyServiceTests
{
    private Mock<IKeyValueRepository<Lobby>> _mockRepo;
    private Mock<IPokerAttackNotificationHelper> _mockHelper;
    private LobbyService _service;
    private CancellationToken _cancellationToken;

    [SetUp]
    public void Setup()
    {
        _mockRepo = new Mock<IKeyValueRepository<Lobby>>();
        _mockHelper = new Mock<IPokerAttackNotificationHelper>();
        _mockRepo.Setup(r => r.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Lobby?>.NotFound());
        _mockRepo.Setup(r => r.AddAsync(It.IsAny<string>(), It.IsAny<Lobby>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success());
        _mockRepo.Setup(r => r.UpdateAsync(It.IsAny<string>(), It.IsAny<Lobby>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success());
        _mockRepo.Setup(r => r.DeleteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success());
        _service = new LobbyService(_mockRepo.Object, _mockHelper.Object,
            Mock.Of<IRepository<Game>>(), Mock.Of<IKeyValueRepository<GameStates>>(),
            Mock.Of<IKeyValueRepository<GameModes>>(), Mock.Of<IKeyValueRepository<ActiveGame>>(),
            Mock.Of<IKeyValueRepository<GamePlayer>>(), Mock.Of<IGameStateMachineService>(),
            NullLogger<LobbyService>.Instance);
        _cancellationToken = CancellationToken.None;
    }

    #region CreateLobbyAsync

    [Test]
    public async Task CreateLobbyAsync_AddsHostToNewLobby()
    {
        // Arrange
        _mockRepo.Setup(r => r.GetAllAsync(_cancellationToken))
                 .ReturnsAsync(Result<IReadOnlyDictionary<string, Lobby>>.Success(new Dictionary<string, Lobby>()));

        // Act
        var result = await _service.CreateLobbyAsync(MakeDto("player1"), _cancellationToken);

        // Assert
        Assert.That(result.Value.Players.Select(p => p.Id), Contains.Item("player1"));
        Assert.That(result.Value.HostPlayer.Id, Is.EqualTo("player1"));
        _mockRepo.Verify(r => r.AddAsync(It.IsAny<string>(), It.IsAny<Lobby>(), _cancellationToken), Times.Once);
    }

    [Test]
    public async Task CreateLobbyAsync_RemovesPlayerFromExistingLobby()
    {
        // Arrange - player1 is the HOST of the existing lobby
        var existingLobby = new Lobby
        {
            Id = "existing",
            HostPlayer = MakePlayer("player1"), // player1 is the host, so lobby should be shut down
            Players = new HashSet<Player> { MakePlayer("player1"), MakePlayer("otherPlayer") }
        };
        _mockRepo.Setup(r => r.GetAllAsync(_cancellationToken))
                 .ReturnsAsync(Result<IReadOnlyDictionary<string, Lobby>>.Success(new Dictionary<string, Lobby> { { "oldLobby", existingLobby } }));
        _mockRepo.Setup(r => r.GetAsync("oldLobby", _cancellationToken))
                 .ReturnsAsync(Result<Lobby?>.Success(existingLobby));

        // Act
        var newLobby = await _service.CreateLobbyAsync(MakeDto("player1"), _cancellationToken);

        // Assert
        // Since player1 is the host of the old lobby, it should be shut down (DeleteAsync called)
        _mockRepo.Verify(r => r.DeleteAsync("oldLobby", _cancellationToken), Times.Once);
        Assert.That(newLobby.Value.Players.Select(p => p.Id), Contains.Item("player1"));
    }

    [Test]
    public async Task CreateLobbyAsync_RemovesNonHostPlayerFromExistingLobby()
    {
        // Arrange - player1 is NOT the host of the existing lobby
        var existingLobby = new Lobby
        {
            Id = "existing",
            HostPlayer = MakePlayer("oldHost"),
            Players = new HashSet<Player> { MakePlayer("oldHost"), MakePlayer("player1"), MakePlayer("otherPlayer") }
        };
        _mockRepo.Setup(r => r.GetAllAsync(_cancellationToken))
                 .ReturnsAsync(Result<IReadOnlyDictionary<string, Lobby>>.Success(new Dictionary<string, Lobby> { { "oldLobby", existingLobby } }));

        // Act
        var newLobby = await _service.CreateLobbyAsync(MakeDto("player1"), _cancellationToken);

        // Assert
        // Since player1 is not the host, it should call UpdateAsync, not DeleteAsync
        _mockRepo.Verify(r => r.UpdateAsync("oldLobby", existingLobby, _cancellationToken), Times.Once);
        _mockRepo.Verify(r => r.DeleteAsync(It.IsAny<string>(), _cancellationToken), Times.Never);
        Assert.That(newLobby.Value.Players.Select(p => p.Id), Contains.Item("player1"));
        // Verify that player1 was removed from the old lobby
        Assert.That(existingLobby.Players.Select(p => p.Id).Contains("player1"), Is.False);
    }

    [Test]
    public async Task CreateLobbyAsync_RemovesHostPlayerFromExistingLobby_ShutsDownOldLobby()
    {
        // Arrange
        var existingLobby = new Lobby
        {
            Id = "existing",
            HostPlayer = MakePlayer("player1"), // player1 is the host of existing lobby
            Players = new HashSet<Player> { MakePlayer("player1"), MakePlayer("otherPlayer") }
        };
        _mockRepo.Setup(r => r.GetAllAsync(_cancellationToken))
                 .ReturnsAsync(Result<IReadOnlyDictionary<string, Lobby>>.Success(new Dictionary<string, Lobby> { { "oldLobby", existingLobby } }));
        _mockRepo.Setup(r => r.GetAsync("oldLobby", _cancellationToken))
                 .ReturnsAsync(Result<Lobby?>.Success(existingLobby));

        // Act
        var newLobby = await _service.CreateLobbyAsync(MakeDto("player1"), _cancellationToken);

        // Assert
        // Since player1 is the host, it should shut down the old lobby
        _mockRepo.Verify(r => r.DeleteAsync("oldLobby", _cancellationToken), Times.Once);
        Assert.That(newLobby.Value.Players.Select(p => p.Id), Contains.Item("player1"));
    }

    #endregion

    #region JoinLobbyAsync

    [Test]
    public async Task JoinLobbyAsync_RemovesPlayerFromPreviousLobby_NonHost()
    {
        // Arrange: player1 is a member, but not host
        var previousLobby = new Lobby
        {
            Id = "existing",
            HostPlayer = MakePlayer("otherHost"),
            Players = new HashSet<Player> { MakePlayer("otherHost"), MakePlayer("player1") }
        };
        var targetLobby = new Lobby
        {
            Id = "existing",
            HostPlayer = MakePlayer("newHost"),
            Players = new HashSet<Player> { MakePlayer("newHost") }
        };

        _mockRepo.Setup(r => r.GetAllAsync(_cancellationToken))
                 .ReturnsAsync(Result<IReadOnlyDictionary<string, Lobby>>.Success(new Dictionary<string, Lobby> { { "prevLobby", previousLobby }, { "targetLobby", targetLobby } }));
        _mockRepo.Setup(r => r.GetAsync("targetLobby", _cancellationToken))
                 .ReturnsAsync(Result<Lobby?>.Success(targetLobby));

        // Act
        await _service.JoinLobbyAsync("targetLobby", MakeDto("player1"), _cancellationToken);

        // Assert
        // Non-host should trigger UpdateAsync
        _mockRepo.Verify(r => r.UpdateAsync("prevLobby", previousLobby, _cancellationToken), Times.Once);
        _mockRepo.Verify(r => r.DeleteAsync(It.IsAny<string>(), _cancellationToken), Times.Never);
        Assert.That(targetLobby.Players.Select(p => p.Id), Contains.Item("player1"));
    }

    [Test]
    public async Task JoinLobbyAsync_RemovesPlayerFromPreviousLobby_Host()
    {
        // Arrange: player1 is the host of previous lobby
        var previousLobby = new Lobby
        {
            Id = "existing",
            HostPlayer = MakePlayer("player1"),
            Players = new HashSet<Player> { MakePlayer("player1"), MakePlayer("otherPlayer") }
        };
        var targetLobby = new Lobby
        {
            Id = "existing",
            HostPlayer = MakePlayer("newHost"),
            Players = new HashSet<Player> { MakePlayer("newHost") }
        };

        _mockRepo.Setup(r => r.GetAllAsync(_cancellationToken))
                 .ReturnsAsync(Result<IReadOnlyDictionary<string, Lobby>>.Success(new Dictionary<string, Lobby> { { "prevLobby", previousLobby }, { "targetLobby", targetLobby } }));
        _mockRepo.Setup(r => r.GetAsync("targetLobby", _cancellationToken))
                 .ReturnsAsync(Result<Lobby?>.Success(targetLobby));
        _mockRepo.Setup(r => r.GetAsync("prevLobby", _cancellationToken))
                 .ReturnsAsync(Result<Lobby?>.Success(previousLobby));

        // Act
        await _service.JoinLobbyAsync("targetLobby", MakeDto("player1"), _cancellationToken);

        // Assert
        // Host leaving should trigger DeleteAsync (shut down previous lobby)
        // Removing the host deletes the previous lobby before updating the target.
        _mockRepo.Verify(r => r.DeleteAsync("prevLobby", _cancellationToken), Times.Once);
        _mockRepo.Verify(r => r.UpdateAsync("targetLobby", targetLobby, _cancellationToken), Times.Once); // for target lobby
        Assert.That(targetLobby.Players.Select(p => p.Id), Contains.Item("player1"));
    }


    [Test]
    public async Task JoinLobbyAsync_ReturnsNotFoundIfLobbyDoesNotExist()
    {
        // Arrange
        _mockRepo.Setup(r => r.GetAsync("nonexistent", _cancellationToken))
                 .ReturnsAsync(Result<Lobby?>.NotFound());

        // Act & Assert
        var result = await _service.JoinLobbyAsync("nonexistent", MakeDto("player1"), _cancellationToken);
        Assert.That(result.Status, Is.EqualTo(ResultStatus.NotFound));
        _mockRepo.Verify(r => r.UpdateAsync(It.IsAny<string>(), It.IsAny<Lobby>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    #endregion

    #region LeaveLobbyAsync

    [Test]
    public async Task LeaveLobbyAsync_RemovesPlayerFromLobby()
    {
        // Arrange
        var lobby = new Lobby
        {
            Id = "existing",
            HostPlayer = MakePlayer("host"),
            Players = new HashSet<Player> { MakePlayer("host"), MakePlayer("player1"), MakePlayer("player2") }
        };
        _mockRepo.Setup(r => r.GetAsync("lobby1", _cancellationToken))
                 .ReturnsAsync(Result<Lobby?>.Success(lobby));

        // Act
        await _service.LeaveLobbyAsync("lobby1", MakeDto("player1"), _cancellationToken);

        // Assert
        Assert.That(lobby.Players.Select(p => p.Id).Contains("player1"), Is.False);
        _mockRepo.Verify(r => r.UpdateAsync("lobby1", lobby, _cancellationToken), Times.Once);
    }

    [Test]
    public async Task LeaveLobbyAsync_HostLeaves_ShutsDownLobby()
    {
        // Arrange
        var lobby = new Lobby
        {
            Id = "existing",
            HostPlayer = MakePlayer("host"),
            Players = new HashSet<Player> { MakePlayer("host"), MakePlayer("player1") }
        };
        _mockRepo.Setup(r => r.GetAsync("lobby1", _cancellationToken))
                 .ReturnsAsync(Result<Lobby?>.Success(lobby));

        // Act
        await _service.LeaveLobbyAsync("lobby1", MakeDto("host"), _cancellationToken);

        // Assert
        _mockRepo.Verify(r => r.DeleteAsync("lobby1", _cancellationToken), Times.Once);
    }

    #endregion

    #region ShutDownLobbyAsync

    [Test]
    public async Task ShutDownLobbyAsync_ReturnsPlayersAndRemovesLobby()
    {
        // Arrange
        var lobby = new Lobby
        {
            Id = "existing",
            HostPlayer = MakePlayer("host"),
            Players = new HashSet<Player> { MakePlayer("host"), MakePlayer("player1") }
        };
        _mockRepo.Setup(r => r.GetAsync("lobby1", _cancellationToken))
                 .ReturnsAsync(Result<Lobby?>.Success(lobby));

        // Act
        var players = await _service.ShutDownLobbyAsync("lobby1", _cancellationToken);

        // Assert
        Assert.That(players.Value.Count(), Is.EqualTo(2));
        _mockRepo.Verify(r => r.DeleteAsync("lobby1", _cancellationToken), Times.Once);
    }

    #endregion

    #region GetPlayersAsync

    [Test]
    public async Task GetPlayersAsync_ReturnsPlayers()
    {
        // Arrange
        var lobby = new Lobby
        {
            Id = "existing",
            HostPlayer = MakePlayer("host"),
            Players = new HashSet<Player> { MakePlayer("host"), MakePlayer("player1") }
        };
        _mockRepo.Setup(r => r.GetAsync("lobby1", _cancellationToken))
                 .ReturnsAsync(Result<Lobby?>.Success(lobby));

        // Act
        var players = await _service.GetPlayersAsync("lobby1", _cancellationToken);

        // Assert
        Assert.That(players.Value.Count(), Is.EqualTo(2));
        Assert.That(players.Value.Select(p => p.Id), Contains.Item("player1"));
    }

    #endregion

    static Player MakePlayer(string id) => new() { Id = id, Name = id };
    static PlayerDTO MakeDto(string id) => new() { Id = id, Name = id };
}