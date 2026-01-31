using Blazored.LocalStorage;
using ChefKnifeStudios.PokerAttack.Client.Shared.Constants;
using CommunityToolkit.Mvvm.ComponentModel;
using System.ComponentModel;

namespace ChefKnifeStudios.PokerAttack.Client.Shared.Services;

public interface ISoloGameDataStore : INotifyPropertyChanged
{
    // Active game (for resumption)
    ActiveGameState? ActiveGame { get; }
    bool HasActiveGame { get; }
    void SetActiveGame(ActiveGameState state);
    void ClearActiveGame();

    // Game result (for post-game display) - replaces ISoloGameResultStore
    GameResult? Result { get; }
    bool HasResult { get; }
    void SetResult(GameResult result);
    void ClearResult();

    // localStorage operations
    bool TryRestoreActiveGameFromLocalStorage();
    bool TryRestoreResultFromLocalStorage();
    int GetRestoredRemainingSeconds();

    // Nested classes
    public class ActiveGameState
    {
        public string? GameId { get; init; }
        public int RemainingSeconds { get; init; }
        public long SavedTimestamp { get; init; }
    }

    public class GameResult
    {
        public int RoundReached { get; init; }
        public int FinalWallet { get; init; }
        public int TotalScore { get; init; }
        public int HandsPlayed { get; init; }
        public int DiscardsUsed { get; init; }
        public int HighestSingleHandScore { get; init; }
        public List<string> PurchasedItemNames { get; init; } = [];
    }
}

public partial class SoloGameDataStore : ObservableObject, ISoloGameDataStore
{
    readonly ISyncLocalStorageService _localStorage;

    public SoloGameDataStore(ISyncLocalStorageService localStorage)
    {
        _localStorage = localStorage;
    }

    [ObservableProperty]
    ISoloGameDataStore.ActiveGameState? _activeGame;

    [ObservableProperty]
    ISoloGameDataStore.GameResult? _result;

    public bool HasActiveGame => ActiveGame is not null;
    public bool HasResult => Result is not null;

    public void SetActiveGame(ISoloGameDataStore.ActiveGameState state)
    {
        ActiveGame = state;
        _localStorage.SetItem(LocalStorageConstants.SoloGameStateKey, state);
    }

    public void ClearActiveGame()
    {
        ActiveGame = null;
        _localStorage.RemoveItem(LocalStorageConstants.SoloGameStateKey);
    }

    public void SetResult(ISoloGameDataStore.GameResult result)
    {
        Result = result;
        _localStorage.SetItem(LocalStorageConstants.SoloGameResultKey, result);
    }

    public void ClearResult()
    {
        Result = null;
        _localStorage.RemoveItem(LocalStorageConstants.SoloGameResultKey);
    }

    public bool TryRestoreActiveGameFromLocalStorage()
    {
        var state = _localStorage.GetItem<ISoloGameDataStore.ActiveGameState>(LocalStorageConstants.SoloGameStateKey);
        if (state?.GameId is null) return false;
        ActiveGame = state;
        return true;
    }

    public bool TryRestoreResultFromLocalStorage()
    {
        var result = _localStorage.GetItem<ISoloGameDataStore.GameResult>(LocalStorageConstants.SoloGameResultKey);
        if (result is null) return false;
        Result = result;
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
