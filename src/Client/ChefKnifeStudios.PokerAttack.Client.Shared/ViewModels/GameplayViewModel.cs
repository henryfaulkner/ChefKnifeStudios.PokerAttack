using ChefKnifeStudios.PokerAttack.Client.Core.Extensions;
using ChefKnifeStudios.PokerAttack.Client.Core.Services;
using ChefKnifeStudios.PokerAttack.Client.Shared.EventArgs;
using ChefKnifeStudios.PokerAttack.Client.Shared.Models;
using ChefKnifeStudios.PokerAttack.Client.Shared.Services;
using ChefKnifeStudios.PokerAttack.Shared;
using ChefKnifeStudios.PokerAttack.Shared.DTOs;
using ChefKnifeStudios.PokerAttack.Shared.DTOs.Gameplay;
using ChefKnifeStudios.PokerAttack.Shared.DTOs.SignalR;
using ChefKnifeStudios.PokerAttack.Shared.Enums;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.AspNetCore.Components;
using System.Collections.ObjectModel;
using System.Text.Json;

namespace ChefKnifeStudios.PokerAttack.Client.Shared.ViewModels;

public interface IGameplayViewModel : IViewModel
{
    string GameId { get; }
    int RunTimeInSeconds { get; }
    int Score { get; }
    ObservableCollection<CardItem> CardsInHand { get; }
    int AvailablePlayHands { get; }
    int AvailableDiscards { get; }
    PlayerPowerDTO? ActivePlayerPower { get; }
    int PowerCharges { get; }

    void Init(string gameId);
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

    [ObservableProperty]
    string _gameId = Guid.Empty.ToString();

    [ObservableProperty]
    int _runTimeInSeconds = 0;

    [ObservableProperty]
    int _score = 0;

    [ObservableProperty]
    ObservableCollection<CardItem> _cardsInHand = [];

    [ObservableProperty]
    int _availablePlayHands = 5;

    [ObservableProperty]
    int _availableDiscards = 5;

    [ObservableProperty]
    PlayerPowerDTO? _activePlayerPower;

    const int _INIT_POWER_CHARGES = 2;
    [ObservableProperty]
    int _powerCharges;

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
        NavigationManager navigationManager)
    {
        _signalRNotificationService = signalRNotificationService;
        _toastService = toastService;
        _eventNotificationService = eventNotificationService;
        _navigationManager = navigationManager;

        _signalRNotificationService.HandleNotificationReceived += HandleSignalRNotificationReceived;
        _eventNotificationService.EventReceived += HandleEventReceived;

        _timerToken = SetInterval(() =>
        {
            if (RunTimeInSeconds > 0) RunTimeInSeconds--;
        }, 1000);
    }

    public void Dispose()
    {
        _signalRNotificationService.HandleNotificationReceived -= HandleSignalRNotificationReceived;
        _timerToken.Cancel();
    }

    public void Init(string gameId)
    {
        GameId = gameId;
    }

    public async Task StartGameAsync(string playerId, CancellationToken cancellationToken = default)
    {
        await _signalRNotificationService.StartGameAsync(GameId, playerId);
    }

    public Task StartRoundAsync(string playerId, CancellationToken cancellationToken = default)
    {
        PowerCharges = _INIT_POWER_CHARGES;
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
        if (index >= 0 && index < CardsInHand.Count)
            CardsInHand[index].IsSelected = !CardsInHand[index].IsSelected;
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
        await _signalRNotificationService.ActivatePlayerPowerAsync(GameId, playerId);
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
                        RunTimeInSeconds = runStartedDTO.RunTimeInSeconds;
                        CardsInHand = runStartedDTO.Cards.Select(x => new CardItem(x)).ToObservableCollection();
                        ApplySort();
                    }
                    break;
                }
            case PokerAttackNotificationType.CardsDealt:
                {
                    var args = JsonSerializer.Deserialize<IEnumerable<CardDTO>>(notification.Payload!, JsonOptions.Get());
                    if (args != null)
                    {
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

    static CancellationTokenSource SetInterval(Action action, int interval)
    {
        var cts = new CancellationTokenSource();
        var token = cts.Token;

        Task.Run(async () =>
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    action();
                    await Task.Delay(interval, token);
                }
                catch (TaskCanceledException)
                {
                    break;
                }
            }
        }, token);

        return cts;
    }
}

