using ChefKnifeStudios.PokerAttack.Client.Core.Extensions;
using ChefKnifeStudios.PokerAttack.Client.Core.Services;
using ChefKnifeStudios.PokerAttack.Client.Shared.Constants;
using ChefKnifeStudios.PokerAttack.Client.Shared.EventArgs;
using ChefKnifeStudios.PokerAttack.Client.Shared.Models;
using ChefKnifeStudios.PokerAttack.Client.Shared.Services;
using ChefKnifeStudios.PokerAttack.Client.Shared.Services.JsInterop;
using ChefKnifeStudios.PokerAttack.Shared;
using ChefKnifeStudios.PokerAttack.Shared.DTOs;
using ChefKnifeStudios.PokerAttack.Shared.DTOs.Gameplay;
using ChefKnifeStudios.PokerAttack.Shared.DTOs.SignalR;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.AspNetCore.Components;
using System.Collections.ObjectModel;
using System.Text.Json;

namespace ChefKnifeStudios.PokerAttack.Client.Shared.ViewModels;

public interface IGameplayViewModel : IViewModel
{
    int RunTimeInSeconds { get; }
    int Score { get; }
    ObservableCollection<CardItem> CardsInHand { get; }
    int AvailablePlayHands { get; }
    int AvailableDiscards { get; }
    PlayerPowerDTO? ActivePlayerPower { get; }
    int PowerCharges { get; }

    Task StartGameAsync(string playerId, CancellationToken cancellationToken = default);
    Task StartRoundAsync(string playerId, CancellationToken cancellationToken = default);
    Task PlaySelectedCardsAsync(string playerId, CancellationToken cancellationToken = default);
    Task DiscardSelectedCardsAsync(string playerId, CancellationToken cancellationToken = default);
    void ToggleCardSelection(int index);
    void SortByRank();
    void SortBySuit();
    void ClearSelections();
    Task ActivatePlayerPowerAsync(string playerId, CancellationToken cancellationToken = default);
}

public partial class GameplayViewModel : BaseViewModel, IGameplayViewModel, IDisposable
{
    readonly ISignalRNotificationService _signalRNotificationService;
    readonly IToastService _toastService;
    readonly IEventNotificationService _eventNotificationService;
    readonly NavigationManager _navigationManager;
    readonly IAudioJsInterop _audioJsInterop;
    readonly IGameDataStore _gameDataStore;

    const int _DEFAULT_NUM_PLAY_HANDS = 5;
    const int _DEFAULT_NUM_DISCARDS = 5;
    const int _DEFAULT_NUM_POWER_CHARGES = 2;

    [ObservableProperty]
    int _runTimeInSeconds = 0;

    [ObservableProperty]
    int _score = 0;

    [ObservableProperty]
    ObservableCollection<CardItem> _cardsInHand = [];

    [ObservableProperty]
    int _availablePlayHands = _DEFAULT_NUM_PLAY_HANDS;

    [ObservableProperty]
    int _availableDiscards = _DEFAULT_NUM_DISCARDS;

    [ObservableProperty]
    PlayerPowerDTO? _activePlayerPower;

    [ObservableProperty]
    int _powerCharges = _DEFAULT_NUM_POWER_CHARGES;

    CancellationTokenSource _timerToken;

    enum SortMode
    {
        None,
        ByRank,
        BySuit
    }

    SortMode _currentSortMode = SortMode.None;

    public GameplayViewModel(
        ISignalRNotificationService signalRNotificationService,
        IToastService toastService,
        IEventNotificationService eventNotificationService,
        NavigationManager navigationManager,
        IAudioJsInterop audioJsInterop,
        IGameDataStore gameDataStore)
    {
        _signalRNotificationService = signalRNotificationService;
        _toastService = toastService;
        _eventNotificationService = eventNotificationService;
        _navigationManager = navigationManager;
        _audioJsInterop = audioJsInterop;
        _gameDataStore = gameDataStore;

        _signalRNotificationService.HandleNotificationReceived += HandleSignalRNotificationReceived;
        _eventNotificationService.EventReceived += HandleEventReceived;

        _timerToken = TimerHelper.SetInterval(() =>
        {
            if (RunTimeInSeconds > 0) RunTimeInSeconds--;
        }, 1000);
    }

    public void Dispose()
    {
        _signalRNotificationService.HandleNotificationReceived -= HandleSignalRNotificationReceived;
        _timerToken.Cancel();
        _timerToken.Dispose();
    }

    public async Task StartGameAsync(string playerId, CancellationToken cancellationToken = default)
    {
        if (_gameDataStore.GameId is null) throw new ApplicationException("GameDataStore.GameId must be set before starting the game.");
        await _signalRNotificationService.StartGameAsync(_gameDataStore.GameId, playerId);
    }

    public Task StartRoundAsync(string playerId, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    public async Task PlaySelectedCardsAsync(string playerId, CancellationToken cancellationToken = default)
    {
        if (AvailablePlayHands <= 0)
        {
            _toastService.ShowWarning("No hands left");
            return;
        }

        var selectedCards = CardsInHand.Where(x => x.IsSelected).ToList();

        if (selectedCards.Count < 1) return;
        if (selectedCards.Count > 5)
        {
            _toastService.ShowWarning("A Hand has a 5 card limit");
            return;
        }

        await _signalRNotificationService.PlayHandAsync(
            playerId,
            selectedCards.Select(x => new CardDTO { Rank = x.Rank, Suit = x.Suit }).ToList()
        );
        AvailablePlayHands--;
    }

    public async Task DiscardSelectedCardsAsync(string playerId, CancellationToken cancellationToken = default)
    {
        if (AvailableDiscards <= 0)
        {
            _toastService.ShowWarning("No hands left");
            return;
        }

        var selectedCards = CardsInHand.Where(x => x.IsSelected).ToList();

        if (selectedCards.Count < 1) return;

        await _signalRNotificationService.DiscardAsync(
            playerId,
            selectedCards.Select(x => new CardDTO { Rank = x.Rank, Suit = x.Suit }).ToList()
        );
        AvailableDiscards--;
    }

    public void ToggleCardSelection(int index)
    {
        if (index < 0 || index >= CardsInHand.Count) return;
        CardsInHand[index].IsSelected = !CardsInHand[index].IsSelected;
        _ = _audioJsInterop.PlayOneShotAsync(FilePaths.PlaceCardThree);
    }

    // --- Sort methods updated to track current mode ---
    public void SortByRank()
    {
        _currentSortMode = SortMode.ByRank;
        ApplySort();
    }

    public void SortBySuit()
    {
        _currentSortMode = SortMode.BySuit;
        ApplySort();
    }

    private void ApplySort()
    {
        if (_currentSortMode == SortMode.None) return;

        var sorted = _currentSortMode switch
        {
            SortMode.ByRank => CardsInHand.OrderBy(c => c.Rank).ThenBy(c => c.Suit).ToList(),
            SortMode.BySuit => CardsInHand.OrderBy(c => c.Suit).ThenBy(c => c.Rank).ToList(),
            _ => CardsInHand.ToList()
        };

        CardsInHand.Clear();
        foreach (var card in sorted)
            CardsInHand.Add(card);
    }

    public void ClearSelections()
    {
        foreach (var card in CardsInHand)
            card.IsSelected = false;
    }

    public async Task ActivatePlayerPowerAsync(string playerId, CancellationToken cancellationToken = default)
    {
        if (PowerCharges <= 0)
        {
            _toastService.ShowWarning("Not enough power charges");
            return;
        }
        if (_gameDataStore.GameId is null) throw new ApplicationException("GameDataStore.GameId must be set before activating power.");
        await _signalRNotificationService.ActivatePlayerPowerAsync(_gameDataStore.GameId, playerId);
        PowerCharges--;
    }

    Task HandleSignalRNotificationReceived(PokerAttackNotification notification)
    {
        switch (notification.NotificationType)
        {
            case PokerAttackNotificationType.RunStarted:
                {
                    var args = JsonSerializer.Deserialize<RunStartedDTO>(notification.Payload!, JsonOptions.Get());
                    if (args is RunStartedDTO runStartedDTO)
                    {
                        RunTimeInSeconds = PokerAttack.Shared.Constants.RoundTimeMs;
                        CardsInHand = runStartedDTO.Cards.Select(x => new CardItem(x)).ToObservableCollection();
                        ApplySort();
                        ResetStats();
                        AvailablePlayHands = runStartedDTO.HandsAvailable;
                        AvailableDiscards = runStartedDTO.DiscardsAvailable;
                    }
                    break;
                }
            case PokerAttackNotificationType.CardsDealt:
                {
                    var args = JsonSerializer.Deserialize<IEnumerable<CardDTO>>(notification.Payload!, JsonOptions.Get());
                    if (args != null)
                    {
                        var (newHand, newCardCount) = ProcessCardsDealt(args, CardsInHand);

                        // Play audio for each newly dealt card
                        if (newCardCount > 0)
                        {
                            Task.Run(async () =>
                            {
                                for (int i = 0; i < newCardCount; i++)
                                {
                                    await _audioJsInterop.PlayOneShotAsync(Constants.FilePaths.SlideCardOne);
                                    await Task.Delay(150); // Stagger the sound effects
                                }
                            });
                        }

                        CardsInHand = args.Select(x => new CardItem(x)).ToObservableCollection();
                        ApplySort();
                    }
                    break;
                }
            case PokerAttackNotificationType.HandPlayed:
                {
                    var args = JsonSerializer.Deserialize<HandResultDTO>(notification.Payload!, JsonOptions.Get());
                    if (args is HandResultDTO handResult)
                    {
                        _toastService.ShowSuccess(
                            $"({string.Join(" + ", handResult.CardValues)} + {handResult.BaseChips}) x {handResult.BaseMultiplier}",
                            $"{handResult.HandType.GetDescription()} - {handResult.HandScore}"
                        );
                        Score = handResult.TotalPlayerScore;
                    }
                    break;
                }
            case PokerAttackNotificationType.WagerCompleted:
                {
                    var args = JsonSerializer.Deserialize<WagerCompletedDTO>(notification.Payload!, JsonOptions.Get());
                    if (args is WagerCompletedDTO wagerCompleted)
                    {
                        _toastService.ShowSuccess(
                            $"+{wagerCompleted.ChipsAwarded} chips!",
                            $"Wager Completed: {wagerCompleted.WagerName}"
                        );
                    }
                    break;
                }
            case PokerAttackNotificationType.GameWon:
                {
                    _navigationManager.NavigateTo($"?gameresult=winner", replace: true);
                    break;
                }
            case PokerAttackNotificationType.GameLost:
                {
                    _navigationManager.NavigateTo($"?gameresult=loser", replace: true);
                    break;
                }
        }
        return Task.CompletedTask;
    }

    Task HandleEventReceived(object sender, IEventArgs args)
    {
        switch (args)
        {
            case PlayerPowerEventArgs playerPowerEventArgs:
                {
                    if (playerPowerEventArgs.Type == PlayerPowerEventArgs.EventTypes.Selection
                        && playerPowerEventArgs.Data is PlayerPowerEventArgs.EventData eventData)
                    {
                        ActivePlayerPower = eventData.PlayerPower;
                    }
                    break;
                }
        }
        return Task.CompletedTask;
    }

    static (List<CardItem> NewHand, int NewCardCount) ProcessCardsDealt(
        IEnumerable<CardDTO> dealtCards,
        IEnumerable<CardItem> currentHand)
    {
        var dealtCardsList = dealtCards.ToList();
        var existingCards = currentHand.Select(c => (c.Rank, c.Suit)).ToHashSet();

        var newCardCount = dealtCardsList.Count(card =>
            !existingCards.Contains((card.Rank, card.Suit)));

        var newHand = dealtCardsList.Select(x => new CardItem(x)).ToList();

        return (newHand, newCardCount);
    }

    void ResetStats()
    {
        Score = 0;
        AvailablePlayHands = _DEFAULT_NUM_PLAY_HANDS;
        AvailableDiscards = _DEFAULT_NUM_DISCARDS;
        PowerCharges = _DEFAULT_NUM_POWER_CHARGES;
    }
}

