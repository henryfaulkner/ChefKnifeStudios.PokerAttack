using Blazored.LocalStorage;
using ChefKnifeStudios.PokerAttack.Client.Core.Services;
using ChefKnifeStudios.PokerAttack.Client.Shared.Constants;
using ChefKnifeStudios.PokerAttack.Client.Shared.Services;
using Moq;

namespace ChefKnifeStudios.PokerAttack.Server.Tests.ComponentTests;

[TestFixture]
public class SignalRReconnectTests
{
    Mock<ISyncLocalStorageService> _mockLocalStorage = null!;
    Mock<ISignalRNotificationService> _mockSignalR = null!;
    MultiGameDataStore _store = null!;

    [SetUp]
    public void Setup()
    {
        _mockLocalStorage = new Mock<ISyncLocalStorageService>();
        _mockSignalR = new Mock<ISignalRNotificationService>();
        _store = new MultiGameDataStore(_mockLocalStorage.Object, _mockSignalR.Object);
    }

    [Test]
    public void SettingGameId_PersistsToLocalStorage_AndInformsSignalRService()
    {
        _store.GameId = "game-1";

        _mockLocalStorage.Verify(l => l.SetItem(LocalStorageConstants.CurrentGameIdKey, "game-1"), Times.Once);
        _mockSignalR.Verify(s => s.SetCurrentIds("game-1", null), Times.Once);
    }

    [Test]
    public void SettingLobbyId_PersistsToLocalStorage_AndInformsSignalRService()
    {
        _store.LobbyId = "lobby-1";

        _mockLocalStorage.Verify(l => l.SetItem(LocalStorageConstants.CurrentLobbyIdKey, "lobby-1"), Times.Once);
        _mockSignalR.Verify(s => s.SetCurrentIds(null, "lobby-1"), Times.Once);
    }

    [Test]
    public void Reset_ClearsGameIdAndLobbyId_FromLocalStorage()
    {
        _store.GameId = "game-1";
        _store.LobbyId = "lobby-1";

        _store.Reset();

        _mockLocalStorage.Verify(l => l.RemoveItem(LocalStorageConstants.CurrentGameIdKey), Times.Once);
        _mockLocalStorage.Verify(l => l.RemoveItem(LocalStorageConstants.CurrentLobbyIdKey), Times.Once);
        Assert.That(_store.GameId, Is.Null);
        Assert.That(_store.LobbyId, Is.Null);
    }

    [Test]
    public void RestoreFromLocalStorage_ReadsGameIdAndLobbyId_SimulatingPageReload()
    {
        _mockLocalStorage.Setup(l => l.GetItem<string>(LocalStorageConstants.CurrentGameIdKey)).Returns("restored-game");
        _mockLocalStorage.Setup(l => l.GetItem<string>(LocalStorageConstants.CurrentLobbyIdKey)).Returns("restored-lobby");

        _store.RestoreFromLocalStorage();

        Assert.That(_store.GameId, Is.EqualTo("restored-game"));
        Assert.That(_store.LobbyId, Is.EqualTo("restored-lobby"));
    }
}
