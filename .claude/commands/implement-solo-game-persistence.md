# Implement Solo Game State Persistence

Implement persistence of solo game state through browser refresh for the Poker Attack Blazor WebAssembly application. Players should automatically return to their game page on refresh rather than going through the Lobbies page.

## Goal

When a player refreshes their browser during a solo game:
1. They should be automatically redirected back to the solo gameplay page
2. The game state should be restored from the server
3. The round timer should be restored (accounting for elapsed time)

## Architecture Context

### Current Solo Play Architecture
- `SoloGameplayViewModel` (`Client.Shared/ViewModels/SoloGameplayViewModel.cs`) manages all game state
- Server has `GET /solo/game-state/{gameId}` endpoint returning `SoloGameStateDTO`
- Game starts via `StartGameAsync(playerName)` - currently always creates new game
- `SoloGameResultStore` (`Client.Shared/Services/SoloGameResultStore.cs`) stores final game results for display

### localStorage Pattern
- Keys defined in `LocalStorageConstants.cs`: PlayerName, Settings, HasSeenTour
- Uses `Blazored.LocalStorage` package (`ISyncLocalStorageService`)
- `SettingsService` provides pattern for localStorage access

## Implementation Steps

### 1. Add LocalStorage Key
**File**: `src/Client/ChefKnifeStudios.PokerAttack.Client.Shared/Constants/LocalStorageConstants.cs`

Add constant:
```csharp
public const string SoloGameStateKey = "SoloGameState";
```

### 2. Create SoloGameDataStore (merging with SoloGameResultStore)
**New File**: `src/Client/ChefKnifeStudios.PokerAttack.Client.Shared/Services/SoloGameDataStore.cs`
**Delete File**: `src/Client/ChefKnifeStudios.PokerAttack.Client.Shared/Services/SoloGameResultStore.cs`

Create unified store with nested classes:

```csharp
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
    int GetRestoredRemainingSeconds();

    // Nested classes
    public class ActiveGameState
    {
        public string? GameId { get; init; }
        public string? PlayerId { get; init; }
        public string? Phase { get; init; }
        public int RoundNumber { get; init; }
        public int Score { get; init; }
        public int Wallet { get; init; }
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
    }

    public void ClearResult()
    {
        Result = null;
    }

    public bool TryRestoreActiveGameFromLocalStorage()
    {
        var state = _localStorage.GetItem<ISoloGameDataStore.ActiveGameState>(LocalStorageConstants.SoloGameStateKey);
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
```

### 3. Add Auto-Redirect on App Startup
**File**: `src/Client/ChefKnifeStudios.PokerAttack.Client.WebApp/App.razor.cs`

Add redirect logic in `OnAfterRenderAsync`:
```csharp
[Inject] ISoloGameDataStore SoloGameDataStore { get; set; } = null!;
[Inject] NavigationManager NavigationManager { get; set; } = null!;

protected override async Task OnAfterRenderAsync(bool firstRender)
{
    if (!firstRender) return;

    var uri = NavigationManager.ToBaseRelativePath(NavigationManager.Uri);
    if (string.IsNullOrEmpty(uri) || uri == "/")
    {
        if (SoloGameDataStore.TryRestoreActiveGameFromLocalStorage() && SoloGameDataStore.HasActiveGame)
        {
            NavigationManager.NavigateTo("/solo-gameplay", forceLoad: false);
        }
    }
}
```

### 4. Modify SoloGameplayViewModel
**File**: `src/Client/ChefKnifeStudios.PokerAttack.Client.Shared/ViewModels/SoloGameplayViewModel.cs`

Changes:
1. Replace `ISoloGameResultStore` with `ISoloGameDataStore` injection
2. Add `ResumeGameAsync()` method to interface and implementation
3. Update `SetActiveGame()` after each play/discard action
4. On GameOver: call `SetResult()` then `ClearActiveGame()`

Add to interface:
```csharp
Task<bool> ResumeGameAsync(CancellationToken cancellationToken = default);
```

Implementation:
```csharp
public async Task<bool> ResumeGameAsync(CancellationToken cancellationToken = default)
{
    if (_soloGameDataStore.ActiveGame?.GameId is null) return false;

    var result = await _endpointsService.GetGameStateAsync(
        _soloGameDataStore.ActiveGame.GameId,
        cancellationToken
    );

    if (!result.IsSuccess || result.Value is null) return false;

    var state = result.Value;

    // Check if game already ended
    if (state.Phase == "GameOver") return false;

    // Populate ViewModel from server state
    GameId = state.GameId;
    PlayerId = state.PlayerId;
    RoundNumber = state.RoundNumber;
    Score = state.Score;
    Wallet = state.Wallet;
    AvailablePlayHands = state.HandsAvailable;
    AvailableDiscards = state.DiscardsAvailable;
    Threshold = CalculateThreshold(RoundNumber);

    if (Enum.TryParse<SoloGamePhase>(state.Phase, out var phase))
        Phase = phase;

    RefreshCardsInHand(state.Cards);

    // Restore timer with elapsed time accounted
    RunTimeInSeconds = _soloGameDataStore.GetRestoredRemainingSeconds();
    if (RunTimeInSeconds <= 0)
    {
        // Time expired - advance phase immediately
        await AdvancePhaseAsync(cancellationToken);
    }
    else
    {
        StartTimer();
    }

    return true;
}
```

Update store after actions (in `PlaySelectedCardsAsync`, `DiscardSelectedCardsAsync`):
```csharp
_soloGameDataStore.SetActiveGame(new ISoloGameDataStore.ActiveGameState
{
    GameId = GameId,
    PlayerId = PlayerId,
    Phase = Phase.ToString(),
    RoundNumber = RoundNumber,
    Score = Score,
    Wallet = Wallet,
    RemainingSeconds = RunTimeInSeconds,
    SavedTimestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
});
```

On GameOver (in `AdvancePhaseAsync` when Phase == GameOver):
```csharp
_soloGameDataStore.SetResult(new ISoloGameDataStore.GameResult
{
    RoundReached = RoundNumber,
    FinalWallet = Wallet,
    TotalScore = TotalScore,
    HandsPlayed = HandsPlayed,
    DiscardsUsed = DiscardsUsed,
    HighestSingleHandScore = HighestSingleHandScore,
    PurchasedItemNames = PurchasedItemNames.ToList()
});
_soloGameDataStore.ClearActiveGame();
```

### 5. Modify SoloGameplay Page
**File**: `src/Client/ChefKnifeStudios.PokerAttack.Client.WebApp/Pages/SoloGameplay.razor.cs`

Update `OnInitializedAsync`:
```csharp
[Inject] ISoloGameDataStore SoloGameDataStore { get; set; } = null!;

protected override async Task OnInitializedAsync()
{
    await base.OnInitializedAsync();

    if (SoloGameDataStore.HasActiveGame)
    {
        var resumed = await SoloGameplayViewModel.ResumeGameAsync();
        if (resumed)
        {
            // Set up event handlers and return
            SoloGameplayViewModel.PropertyChanged += ViewModel_OnPropertyChanged;
            SoloGameplayViewModel.CardsInHand.CollectionChanged += CardsInHand_CollectionChanged;
            SoloGameplayViewModel.ShopItems.CollectionChanged += ShopItems_CollectionChanged;
            return;
        }
        SoloGameDataStore.ClearActiveGame();
    }

    var playerName = ApplicationViewModel.Player?.Name ?? "Player";
    await SoloGameplayViewModel.StartGameAsync(playerName);

    SoloGameplayViewModel.PropertyChanged += ViewModel_OnPropertyChanged;
    SoloGameplayViewModel.CardsInHand.CollectionChanged += CardsInHand_CollectionChanged;
    SoloGameplayViewModel.ShopItems.CollectionChanged += ShopItems_CollectionChanged;
}
```

### 6. Update References to SoloGameResultStore
Replace `ISoloGameResultStore` with `ISoloGameDataStore` in:
- `src/Client/ChefKnifeStudios.PokerAttack.Client.WebApp/Pages/Lobbies.razor.cs`
- `src/Client/ChefKnifeStudios.PokerAttack.Client.Shared/Components/Modals/SoloGameResultModal.razor`

### 7. Update DI Registration
**File**: `src/Client/ChefKnifeStudios.PokerAttack.Client.WebApp/Program.cs`

Replace:
```csharp
builder.Services.AddScoped<ISoloGameResultStore, SoloGameResultStore>();
```
With:
```csharp
builder.Services.AddScoped<ISoloGameDataStore, SoloGameDataStore>();
```

## Edge Cases

1. **Game Not Found (404)**: `ResumeGameAsync` returns false, clear stale state, start new game
2. **Game Already Ended**: Check phase in response, if GameOver clear state and navigate to Lobbies
3. **Timer Expired**: If `GetRestoredRemainingSeconds() <= 0`, trigger immediate phase advance

## Files Summary

| File | Action |
|------|--------|
| `Client.Shared/Constants/LocalStorageConstants.cs` | Add SoloGameStateKey |
| `Client.Shared/Services/SoloGameDataStore.cs` | **Create** |
| `Client.Shared/Services/SoloGameResultStore.cs` | **Delete** |
| `Client.Shared/ViewModels/SoloGameplayViewModel.cs` | Add ResumeGameAsync, update store |
| `Client.WebApp/Pages/SoloGameplay.razor.cs` | Check for saved game |
| `Client.WebApp/Pages/Lobbies.razor.cs` | Update ISoloGameResultStore → ISoloGameDataStore |
| `Client.Shared/Components/Modals/SoloGameResultModal.razor` | Update ISoloGameResultStore → ISoloGameDataStore |
| `Client.WebApp/App.razor.cs` | Add redirect logic |
| `Client.WebApp/Program.cs` | Update DI registration |
