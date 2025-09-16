using ChefKnifeStudios.PokerAttack.Server.BL.Services;
using ChefKnifeStudios.PokerAttack.Server.Core.Interfaces;
using ChefKnifeStudios.PokerAttack.Server.Core.Models;
using Moq;

namespace ChefKnifeStudios.PokerAttack.Server.Tests.UnitTests;

[TestFixture]
public class LobbyServiceTests
{
    private Mock<ILobbyRepository> _mockRepo;
    private LobbyService _service;
    private CancellationToken _cancellationToken;

    [SetUp]
    public void Setup()
    {
        _mockRepo = new Mock<ILobbyRepository>();
        _service = new LobbyService(_mockRepo.Object);
        _cancellationToken = CancellationToken.None;
    }

    #region CreateLobbyAsync

    [Test]
    public async Task CreateLobbyAsync_AddsHostToNewLobby()
    {
        // Arrange
        _mockRepo.Setup(r => r.GetAllLobbiesAsync(_cancellationToken))
                 .ReturnsAsync(new Dictionary<string, Lobby>());

        // Act
        var result = await _service.CreateLobbyAsync("player1", _cancellationToken);

        // Assert
        Assert.That(result.PlayerIds, Contains.Item("player1"));
        Assert.That(result.HostPlayerId, Is.EqualTo("player1"));
        _mockRepo.Verify(r => r.AddLobbyAsync(It.IsAny<string>(), It.IsAny<Lobby>(), _cancellationToken), Times.Once);
    }

    [Test]
    public async Task CreateLobbyAsync_RemovesPlayerFromExistingLobby()
    {
        // Arrange - player1 is the HOST of the existing lobby
        var existingLobby = new Lobby
        {
            HostPlayerId = "player1", // player1 is the host, so lobby should be shut down
            PlayerIds = new HashSet<string> { "player1", "otherPlayer" }
        };
        _mockRepo.Setup(r => r.GetAllLobbiesAsync(_cancellationToken))
                 .ReturnsAsync(new Dictionary<string, Lobby> { { "oldLobby", existingLobby } });
        _mockRepo.Setup(r => r.GetLobbyAsync("oldLobby", _cancellationToken))
                 .ReturnsAsync(existingLobby);

        // Act
        var newLobby = await _service.CreateLobbyAsync("player1", _cancellationToken);

        // Assert
        // Since player1 is the host of the old lobby, it should be shut down (RemoveLobbyAsync called)
        _mockRepo.Verify(r => r.RemoveLobbyAsync("oldLobby", _cancellationToken), Times.Once);
        Assert.That(newLobby.PlayerIds, Contains.Item("player1"));
    }

    [Test]
    public async Task CreateLobbyAsync_RemovesNonHostPlayerFromExistingLobby()
    {
        // Arrange - player1 is NOT the host of the existing lobby
        var existingLobby = new Lobby
        {
            HostPlayerId = "oldHost",
            PlayerIds = new HashSet<string> { "oldHost", "player1", "otherPlayer" }
        };
        _mockRepo.Setup(r => r.GetAllLobbiesAsync(_cancellationToken))
                 .ReturnsAsync(new Dictionary<string, Lobby> { { "oldLobby", existingLobby } });

        // Act
        var newLobby = await _service.CreateLobbyAsync("player1", _cancellationToken);

        // Assert
        // Since player1 is not the host, it should call UpdateLobbyAsync, not RemoveLobbyAsync
        _mockRepo.Verify(r => r.UpdateLobbyAsync("oldLobby", existingLobby, _cancellationToken), Times.Once);
        _mockRepo.Verify(r => r.RemoveLobbyAsync(It.IsAny<string>(), _cancellationToken), Times.Never);
        Assert.That(newLobby.PlayerIds, Contains.Item("player1"));
        // Verify that player1 was removed from the old lobby
        Assert.That(existingLobby.PlayerIds.Contains("player1"), Is.False);
    }

    [Test]
    public async Task CreateLobbyAsync_RemovesHostPlayerFromExistingLobby_ShutsDownOldLobby()
    {
        // Arrange
        var existingLobby = new Lobby
        {
            HostPlayerId = "player1", // player1 is the host of existing lobby
            PlayerIds = new HashSet<string> { "player1", "otherPlayer" }
        };
        _mockRepo.Setup(r => r.GetAllLobbiesAsync(_cancellationToken))
                 .ReturnsAsync(new Dictionary<string, Lobby> { { "oldLobby", existingLobby } });
        _mockRepo.Setup(r => r.GetLobbyAsync("oldLobby", _cancellationToken))
                 .ReturnsAsync(existingLobby);

        // Act
        var newLobby = await _service.CreateLobbyAsync("player1", _cancellationToken);

        // Assert
        // Since player1 is the host, it should shut down the old lobby
        _mockRepo.Verify(r => r.RemoveLobbyAsync("oldLobby", _cancellationToken), Times.Once);
        Assert.That(newLobby.PlayerIds, Contains.Item("player1"));
    }

    #endregion

    #region JoinLobbyAsync

    [Test]
    public async Task JoinLobbyAsync_RemovesPlayerFromPreviousLobby_NonHost()
    {
        // Arrange: player1 is a member, but not host
        var previousLobby = new Lobby
        {
            HostPlayerId = "otherHost",
            PlayerIds = new HashSet<string> { "otherHost", "player1" }
        };
        var targetLobby = new Lobby
        {
            HostPlayerId = "newHost",
            PlayerIds = new HashSet<string>() { "newHost" }
        };

        _mockRepo.Setup(r => r.GetAllLobbiesAsync(_cancellationToken))
                 .ReturnsAsync(new Dictionary<string, Lobby> { { "prevLobby", previousLobby }, { "targetLobby", targetLobby } });
        _mockRepo.Setup(r => r.GetLobbyAsync("targetLobby", _cancellationToken))
                 .ReturnsAsync(targetLobby);

        // Act
        await _service.JoinLobbyAsync("targetLobby", "player1", _cancellationToken);

        // Assert
        // Non-host should trigger UpdateLobbyAsync
        _mockRepo.Verify(r => r.UpdateLobbyAsync("prevLobby", previousLobby, _cancellationToken), Times.Once);
        _mockRepo.Verify(r => r.RemoveLobbyAsync(It.IsAny<string>(), _cancellationToken), Times.Never);
        Assert.That(targetLobby.PlayerIds, Contains.Item("player1"));
    }

    [Test]
    public async Task JoinLobbyAsync_RemovesPlayerFromPreviousLobby_Host()
    {
        // Arrange: player1 is the host of previous lobby
        var previousLobby = new Lobby
        {
            HostPlayerId = "player1",
            PlayerIds = new HashSet<string> { "player1", "otherPlayer" }
        };
        var targetLobby = new Lobby
        {
            HostPlayerId = "newHost",
            PlayerIds = new HashSet<string>() { "newHost" }
        };

        _mockRepo.Setup(r => r.GetAllLobbiesAsync(_cancellationToken))
                 .ReturnsAsync(new Dictionary<string, Lobby> { { "prevLobby", previousLobby }, { "targetLobby", targetLobby } });
        _mockRepo.Setup(r => r.GetLobbyAsync("targetLobby", _cancellationToken))
                 .ReturnsAsync(targetLobby);
        _mockRepo.Setup(r => r.GetLobbyAsync("prevLobby", _cancellationToken))
                 .ReturnsAsync(previousLobby);

        // Act
        await _service.JoinLobbyAsync("targetLobby", "player1", _cancellationToken);

        // Assert
        // Host leaving should trigger RemoveLobbyAsync (shut down previous lobby)
        // Note: ShutDownLobbyAsync calls GetLobbyAsync first, then RemoveLobbyAsync
        _mockRepo.Verify(r => r.GetLobbyAsync("prevLobby", _cancellationToken), Times.Once);
        _mockRepo.Verify(r => r.RemoveLobbyAsync("prevLobby", _cancellationToken), Times.Once);
        _mockRepo.Verify(r => r.UpdateLobbyAsync("targetLobby", targetLobby, _cancellationToken), Times.Once); // for target lobby
        Assert.That(targetLobby.PlayerIds, Contains.Item("player1"));
    }


    [Test]
    public void JoinLobbyAsync_ThrowsIfLobbyDoesNotExist()
    {
        // Arrange
        _mockRepo.Setup(r => r.GetLobbyAsync("nonexistent", _cancellationToken))
                 .ReturnsAsync((Lobby?)null);

        // Act & Assert
        Assert.ThrowsAsync<KeyNotFoundException>(async () =>
            await _service.JoinLobbyAsync("nonexistent", "player1", _cancellationToken));
    }

    #endregion

    #region LeaveLobbyAsync

    [Test]
    public async Task LeaveLobbyAsync_RemovesPlayerFromLobby()
    {
        // Arrange
        var lobby = new Lobby
        {
            HostPlayerId = "host",
            PlayerIds = new HashSet<string> { "host", "player1", "player2" }
        };
        _mockRepo.Setup(r => r.GetLobbyAsync("lobby1", _cancellationToken))
                 .ReturnsAsync(lobby);

        // Act
        await _service.LeaveLobbyAsync("lobby1", "player1", _cancellationToken);

        // Assert
        Assert.That(lobby.PlayerIds.Contains("player1"), Is.False);
        _mockRepo.Verify(r => r.UpdateLobbyAsync("lobby1", lobby, _cancellationToken), Times.Once);
    }

    [Test]
    public async Task LeaveLobbyAsync_HostLeaves_ShutsDownLobby()
    {
        // Arrange
        var lobby = new Lobby
        {
            HostPlayerId = "host",
            PlayerIds = new HashSet<string> { "host", "player1" }
        };
        _mockRepo.Setup(r => r.GetLobbyAsync("lobby1", _cancellationToken))
                 .ReturnsAsync(lobby);

        // Act
        await _service.LeaveLobbyAsync("lobby1", "host", _cancellationToken);

        // Assert
        _mockRepo.Verify(r => r.RemoveLobbyAsync("lobby1", _cancellationToken), Times.Once);
    }

    #endregion

    #region ShutDownLobbyAsync

    [Test]
    public async Task ShutDownLobbyAsync_ReturnsPlayersAndRemovesLobby()
    {
        // Arrange
        var lobby = new Lobby
        {
            HostPlayerId = "host",
            PlayerIds = new HashSet<string> { "host", "player1" }
        };
        _mockRepo.Setup(r => r.GetLobbyAsync("lobby1", _cancellationToken))
                 .ReturnsAsync(lobby);

        // Act
        var players = await _service.ShutDownLobbyAsync("lobby1", _cancellationToken);

        // Assert
        Assert.That(players.Count(), Is.EqualTo(2));
        _mockRepo.Verify(r => r.RemoveLobbyAsync("lobby1", _cancellationToken), Times.Once);
    }

    #endregion

    #region GetPlayersAsync

    [Test]
    public async Task GetPlayersAsync_ReturnsPlayers()
    {
        // Arrange
        var lobby = new Lobby
        {
            HostPlayerId = "host",
            PlayerIds = new HashSet<string> { "host", "player1" }
        };
        _mockRepo.Setup(r => r.GetLobbyAsync("lobby1", _cancellationToken))
                 .ReturnsAsync(lobby);

        // Act
        var players = await _service.GetPlayersAsync("lobby1", _cancellationToken);

        // Assert
        Assert.That(players.Count(), Is.EqualTo(2));
        Assert.That(players, Contains.Item("player1"));
    }

    #endregion
}