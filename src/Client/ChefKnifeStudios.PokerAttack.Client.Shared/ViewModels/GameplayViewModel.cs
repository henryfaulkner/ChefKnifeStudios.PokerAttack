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
    int Score { get; }
    ObservableCollection<CardItem> CardsInHand { get; }
    Task StartRunAsync(string playerId, CancellationToken cancellationToken = default);
}

public partial class GameplayViewModel : BaseViewModel, IGameplayViewModel, IDisposable
{
    readonly ISignalRNotificationService _signalRNotificationService;
    readonly IToastService _toastService;

    [ObservableProperty]
    int _score = 0;

    [ObservableProperty]
    ObservableCollection<CardItem> _cardsInHand = [];

    public GameplayViewModel(
        ISignalRNotificationService signalRNotificationService,
        IToastService toastService)
    {
        _signalRNotificationService = signalRNotificationService;
        _toastService = toastService;

        _signalRNotificationService.HandleNotificationReceived += HandleSignalRNotificationReceived;
    }

    public void Dispose()
    {
        _signalRNotificationService.HandleNotificationReceived -= HandleSignalRNotificationReceived;
    }

    public async Task StartRunAsync(string playerId, CancellationToken cancellationToken = default)
    {
        await _signalRNotificationService.StartRunAsync(playerId);
    }

    Task HandleSignalRNotificationReceived(PokerAttackNotification notification)
    {
        switch (notification.NotificationType)
        {
            case PokerAttackNotificationType.RunStarted:
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
                        _toastService.ShowSuccess($"{handResult.BaseChips} x {handResult.BaseMultiplier}", handResult.HandType.ToString());
                        Score = handResult.TotalPlayerScore;
                    }
                    break;
                }
        }
        return Task.CompletedTask;
    }
}
