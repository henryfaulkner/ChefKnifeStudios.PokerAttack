using Blazored.LocalStorage;
using ChefKnifeStudios.PokerAttack.Client.Core.Services;
using ChefKnifeStudios.PokerAttack.Client.Shared.Constants;
using ChefKnifeStudios.PokerAttack.Client.Shared.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.ObjectModel;
using System.ComponentModel;

namespace ChefKnifeStudios.PokerAttack.Client.Shared.Services;

public interface IMultiGameDataStore : INotifyPropertyChanged
{
    string? GameId { get; set; }
    string? LobbyId { get; set; }
    int Wallet { get; set; }
    bool IsLoadingWallet { get; set; }
    ObservableCollection<ShopItem> ShopItems { get; set; }
    ObservableCollection<ShopItem> PlayerItems { get; set; }
    bool IsLoadingShop { get; set; }
    ObservableCollection<PlayerPowerListItem> PlayerPowers { get; set; }
    bool IsLoadingPlayerPowers { get; set; }
    ObservableCollection<ScoreboardListItem> ScoreboardItems { get; set; }
    bool IsLoadingScoreboard { get; set; }
    void Reset();
    void RestoreFromLocalStorage();
}

public partial class MultiGameDataStore : ObservableObject, IMultiGameDataStore
{
    readonly ISyncLocalStorageService _localStorage;
    readonly ISignalRNotificationService _signalRNotificationService;

    [ObservableProperty]
    string? _gameId;

    [ObservableProperty]
    string? _lobbyId;

    [ObservableProperty]
    int _wallet;

    [ObservableProperty]
    bool _isLoadingWallet;

    [ObservableProperty]
    ObservableCollection<ShopItem> _shopItems = [];

    [ObservableProperty]
    ObservableCollection<ShopItem> _playerItems = [];

    [ObservableProperty]
    bool _isLoadingShop;

    [ObservableProperty]
    ObservableCollection<PlayerPowerListItem> _playerPowers = [];

    [ObservableProperty]
    bool _isLoadingPlayerPowers;

    [ObservableProperty]
    ObservableCollection<ScoreboardListItem> _scoreboardItems = [];

    [ObservableProperty]
    bool _isLoadingScoreboard;

    public MultiGameDataStore(
        ISyncLocalStorageService localStorage,
        ISignalRNotificationService signalRNotificationService)
    {
        _localStorage = localStorage;
        _signalRNotificationService = signalRNotificationService;
        Reset();
    }

    partial void OnGameIdChanged(string? value)
    {
        if (value is null)
            _localStorage.RemoveItem(LocalStorageConstants.CurrentGameIdKey);
        else
            _localStorage.SetItem(LocalStorageConstants.CurrentGameIdKey, value);

        _signalRNotificationService.SetCurrentIds(value, LobbyId);
    }

    partial void OnLobbyIdChanged(string? value)
    {
        if (value is null)
            _localStorage.RemoveItem(LocalStorageConstants.CurrentLobbyIdKey);
        else
            _localStorage.SetItem(LocalStorageConstants.CurrentLobbyIdKey, value);

        _signalRNotificationService.SetCurrentIds(GameId, value);
    }

    public void Reset()
    {
        // Reset all properties to default values
        // This will be called when visiting the Game page
        GameId = null;
        LobbyId = null;
        Wallet = 0;
        IsLoadingWallet = false;
        ShopItems = [];
        IsLoadingShop = false;
        PlayerPowers = [];
        IsLoadingPlayerPowers = false;
        ScoreboardItems = [];
        IsLoadingScoreboard = false;
    }

    public void RestoreFromLocalStorage()
    {
        GameId = _localStorage.GetItem<string>(LocalStorageConstants.CurrentGameIdKey);
        LobbyId = _localStorage.GetItem<string>(LocalStorageConstants.CurrentLobbyIdKey);
    }
}
