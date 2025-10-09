using ChefKnifeStudios.PokerAttack.Client.Core.Services;
using ChefKnifeStudios.PokerAttack.Client.Shared.EventArgs;
using ChefKnifeStudios.PokerAttack.Client.Shared.Models;
using ChefKnifeStudios.PokerAttack.Client.Shared.Services;
using ChefKnifeStudios.PokerAttack.Shared;
using ChefKnifeStudios.PokerAttack.Shared.DTOs.Gameplay;
using ChefKnifeStudios.PokerAttack.Shared.DTOs.SignalR;
using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.ObjectModel;
using System.Text.Json;

namespace ChefKnifeStudios.PokerAttack.Client.Shared.ViewModels;

public interface IGameplayViewModel : IViewModel
{
    string GameId { get; }
    int RunTimeInSeconds { get; }
    int Score { get; }
    ObservableCollection<CardItem> CardsInHand { get; }
    void Init(string gameId);
    Task StartRoundAsync(string playerId, CancellationToken cancellationToken = default);
    Task PlaySelectedCardsAsync(string playerId, CancellationToken cancellationToken = default);
    Task DiscardSelectedCardsAsync(string playerId, CancellationToken cancellationToken = default);
    void ToggleCardSelection(int index);
    void SortByRank();
    void SortBySuit();
    void ClearSelections();
}

public partial class GameplayViewModel : BaseViewModel, IGameplayViewModel, IDisposable
{
    readonly ISignalRNotificationService _signalRNotificationService;
    readonly IToastService _toastService;
    readonly IEventNotificationService _eventNotificationService;

    [ObservableProperty]
    string _gameId = Guid.Empty.ToString();

    [ObservableProperty]
    int _runTimeInSeconds = 0;

    [ObservableProperty]
    int _score = 0;

    [ObservableProperty]
    ObservableCollection<CardItem> _cardsInHand = [];

    CancellationTokenSource _timerToken;

    public GameplayViewModel(
        ISignalRNotificationService signalRNotificationService,
        IToastService toastService,
        IEventNotificationService eventNotificationService)
    {
        _signalRNotificationService = signalRNotificationService;
        _toastService = toastService;

        _signalRNotificationService.HandleNotificationReceived += HandleSignalRNotificationReceived;
        _timerToken = SetInterval(() =>
        {
            if (RunTimeInSeconds > 0) RunTimeInSeconds--;
        }, 1000);
        _eventNotificationService = eventNotificationService;
    }

    public void Dispose()
    {
        _signalRNotificationService.HandleNotificationReceived -= HandleSignalRNotificationReceived;
        _timerToken.Cancel();
    }

    public void Init(string gameId) => GameId = gameId;

    public async Task StartRoundAsync(string playerId, CancellationToken cancellationToken = default)
    {
        await _signalRNotificationService.StartRoundAsync(GameId, playerId);
    }

    public async Task PlaySelectedCardsAsync(string playerId, CancellationToken cancellationToken = default)
    {
        var selectedCards = CardsInHand.Where(x => x.IsSelected).ToList();

        if (selectedCards.Count() < 1)
        {
            return;
        }
        if (selectedCards.Count() > 5)
        {
            _toastService.ShowWarning("A Hand has a 5 card limit");
            return;
        }

        await _signalRNotificationService.PlayHandAsync(
            playerId,
            selectedCards
                .Select(x => new CardDTO { Rank = x.Rank, Suit = x.Suit, })
                .ToList()
        );

        foreach (var selectedCard in selectedCards) CardsInHand.Remove(selectedCard);
        await _signalRNotificationService.DealHandAsync(playerId, selectedCards.Count());
    }

    public async Task DiscardSelectedCardsAsync(string playerId, CancellationToken cancellationToken = default)
    {
        var selectedCards = CardsInHand.Where(x => x.IsSelected).ToList();
        foreach (var selectedCard in selectedCards) CardsInHand.Remove(selectedCard);
        await _signalRNotificationService.DealHandAsync(playerId, selectedCards.Count());
    }

    public void ToggleCardSelection(int index)
    {
        if (index >= 0 && index < CardsInHand.Count)
            CardsInHand[index].IsSelected = !CardsInHand[index].IsSelected;
    }

    public void SortByRank()
    {
        // Sort ascending by rank, then by suit to make ordering stable
        var sorted = CardsInHand.OrderBy(c => c.Rank).ThenBy(c => c.Suit).ToList();

        // Reorder the observable collection (not replace it, to trigger UI update correctly)
        CardsInHand.Clear();
        foreach (var card in sorted)
            CardsInHand.Add(card);
    }

    public void SortBySuit()
    {
        // Sort ascending by suit, then by rank
        var sorted = CardsInHand.OrderBy(c => c.Suit).ThenBy(c => c.Rank).ToList();

        CardsInHand.Clear();
        foreach (var card in sorted)
            CardsInHand.Add(card);
    }

    public void ClearSelections()
    {
        foreach (var card in CardsInHand)
        {
            if (card.IsSelected)
                card.IsSelected = false;
        }
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
                        foreach (CardItem card in runStartedDTO.Cards.Select(x => new CardItem(x)))
                        {
                            CardsInHand.Add(card);
                        }
                    }
                    break;
                }
            case PokerAttackNotificationType.CardsDealt:
                {
                    var args = JsonSerializer.Deserialize<IEnumerable<CardDTO>>(notification.Payload!, JsonOptions.Get());
                    if (args is IEnumerable<CardDTO> newCards)
                    {
                        foreach (CardItem card in newCards.Select(x => new CardItem(x)))
                        {
                            CardsInHand.Add(card);
                        }
                    }
                    break;
                }
            case PokerAttackNotificationType.HandPlayed:
                {
                    var args = JsonSerializer.Deserialize<HandResultDTO>(notification.Payload!, JsonOptions.Get());
                    if (args is HandResultDTO handResult)
                    {
                        
                        _toastService.ShowSuccess($"({string.Join(" + ", handResult.CardValues)} + {handResult.BaseChips}) x {handResult.BaseMultiplier}", $"{handResult.HandType.GetDescription()} - {handResult.HandScore}");
                        Score = handResult.TotalPlayerScore;
                    }
                    break;
                }
            case PokerAttackNotificationType.RoundEnded:
                {
                    _eventNotificationService.PostEvent(
                        this,
                        new GameTransitionEventArgs
                        { 
                            Data = new () { GameEvent = GameEvents.Next },
                        }
                    );
                    break;
                }
        }
        return Task.CompletedTask;
    }

    static CancellationTokenSource SetInterval(Action action, int interval)
    {
        var cancellationTokenSource = new CancellationTokenSource();
        var cancellationToken = cancellationTokenSource.Token;

        Task.Run(async () =>
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    action();
                    await Task.Delay(interval, cancellationToken);
                }
                catch (TaskCanceledException ex)
                {
                    // Task was canceled
                    Console.WriteLine($"SetInterval still running after cancellation. {ex.Message}");
                    break;
                }
            }
        }, cancellationToken);

        return cancellationTokenSource;
    }
}
