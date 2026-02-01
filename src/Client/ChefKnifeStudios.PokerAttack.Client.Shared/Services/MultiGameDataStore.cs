using Blazored.LocalStorage;
using ChefKnifeStudios.PokerAttack.Client.Shared.Constants;
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

    // Active game state (for resumption after refresh)
    ActiveGameState? ActiveGame { get; }
    bool HasActiveGame { get; }
    void SetActiveGame(ActiveGameState state);
    void ClearActiveGame();

    // localStorage operations
    bool TryRestoreActiveGameFromLocalStorage();
    int GetRestoredRemainingSeconds();

    // Nested class for persistence
    public class ActiveGameState
    {
        public string? GameId { get; init; }
        public string? PlayerId { get; init; }
        public int RemainingSeconds { get; init; }
        public long SavedTimestamp { get; init; }
    }
}

public partial class MultiGameDataStore : ObservableObject, IMultiGameDataStore
{
    readonly ISyncLocalStorageService _localStorage;

    public MultiGameDataStore(ISyncLocalStorageService localStorage)
    {
        _localStorage = localStorage;
    }

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

    [ObservableProperty]
    IMultiGameDataStore.ActiveGameState? _activeGame;

    public bool HasActiveGame => ActiveGame is not null;

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
        // Note: Don't reset ActiveGame here - it's managed separately for persistence
    }

    public void SetActiveGame(IMultiGameDataStore.ActiveGameState state)
    {
        ActiveGame = state;
        _localStorage.SetItem(LocalStorageConstants.MultiGameStateKey, state);
    }

    public void ClearActiveGame()
    {
        ActiveGame = null;
        _localStorage.RemoveItem(LocalStorageConstants.MultiGameStateKey);
    }

    public bool TryRestoreActiveGameFromLocalStorage()
    {
        var state = _localStorage.GetItem<IMultiGameDataStore.ActiveGameState>(LocalStorageConstants.MultiGameStateKey);
        if (state?.GameId is null) return false;
        ActiveGame = state;
        return true;
    }

    public int GetRestoredRemainingSeconds()
    {
        if (ActiveGame is null) return 0;
        var elapsed = DateTimeOffset.UtcNow.ToUnixTimeSeconds() - ActiveGame.SavedTimestamp;
        var remaining = ActiveGame.RemainingSeconds - (int)elapsed;
        return Math.Max(0, remaining);
    }
}
