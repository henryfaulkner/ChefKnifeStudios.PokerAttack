using ChefKnifeStudios.PokerAttack.Client.Core.Services;
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
    int RunTimeInSeconds { get; }
    int Score { get; }
    ObservableCollection<CardItem> CardsInHand { get; }
    Task StartRunAsync(string playerId, CancellationToken cancellationToken = default);
    Task PlaySelectedCardsAsync(string playerId, CancellationToken cancellationToken = default);
    Task DiscardSelectedCardsAsync(string playerId, CancellationToken cancellationToken = default);
    void ToggleCardSelection(int index);
}

public partial class GameplayViewModel : BaseViewModel, IGameplayViewModel, IDisposable
{
    readonly ISignalRNotificationService _signalRNotificationService;
    readonly IToastService _toastService;

    [ObservableProperty]
    int _runTimeInSeconds = 0;

    [ObservableProperty]
    int _score = 0;

    [ObservableProperty]
    ObservableCollection<CardItem> _cardsInHand = [];

    CancellationTokenSource _timerToken;

    public GameplayViewModel(
        ISignalRNotificationService signalRNotificationService,
        IToastService toastService)
    {
        _signalRNotificationService = signalRNotificationService;
        _toastService = toastService;

        _signalRNotificationService.HandleNotificationReceived += HandleSignalRNotificationReceived;
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

    public async Task StartRunAsync(string playerId, CancellationToken cancellationToken = default)
    {
        await _signalRNotificationService.StartRunAsync(playerId);
    }

    public async Task PlaySelectedCardsAsync(string playerId, CancellationToken cancellationToken = default)
    {
        var selectedCards = CardsInHand.Where(x => x.IsSelected).ToList();
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
