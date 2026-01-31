using ChefKnifeStudios.PokerAttack.Client.Shared.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.ObjectModel;
using System.ComponentModel;

namespace ChefKnifeStudios.PokerAttack.Client.Shared.Services;

public interface IMultiGameDataStore : INotifyPropertyChanged
{
    string? GameId { get; set; }
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
}

public partial class MultiGameDataStore : ObservableObject, IMultiGameDataStore
{
    [ObservableProperty]
    string? _gameId;

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

    public MultiGameDataStore()
    {
        Reset();
    }

    public void Reset()
    {
        // Reset all properties to default values
        // This will be called when visiting the Game page
        GameId = null;
        Wallet = 0;
        IsLoadingWallet = false;
        ShopItems = [];
        IsLoadingShop = false;
        PlayerPowers = [];
        IsLoadingPlayerPowers = false;
        ScoreboardItems = [];
        IsLoadingScoreboard = false;
    }
}
