using ChefKnifeStudios.PokerAttack.Client.Core.Services;
using ChefKnifeStudios.PokerAttack.Client.Shared.EventArgs;
using ChefKnifeStudios.PokerAttack.Shared.DTOs.SignalR;
using ChefKnifeStudios.PokerAttack.Shared;
using ChefKnifeStudios.PokerAttack.Shared.Enums;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace ChefKnifeStudios.PokerAttack.Client.Shared.ViewModels;

public interface IGameStateMachineViewModel : IViewModel
{
    GameStates GameState { get; }
}

public partial class GameStateMachineViewModel : BaseViewModel, IGameStateMachineViewModel, IDisposable
{
    readonly ILogger<GameStateMachineViewModel> _logger;
    readonly IApplicationViewModel _applicationViewModel;
    readonly ISignalRNotificationService _signalRNotificationService;
    readonly IEventNotificationService _eventNotificationService;

    [ObservableProperty]
    public GameStates _gameState = GameStates.Upgrade;

    public GameStateMachineViewModel(
        ILogger<GameStateMachineViewModel> logger,
        IApplicationViewModel applicationViewModel,
        ISignalRNotificationService signalRNotificationService,
        IEventNotificationService eventNotificationService)
    {
        _logger = logger;
        _applicationViewModel = applicationViewModel;
        _signalRNotificationService = signalRNotificationService;
        _eventNotificationService = eventNotificationService;

        _signalRNotificationService.HandleNotificationReceived += HandleSignalRNotificationReceived;
        _eventNotificationService.EventReceived += HandleEventReceived;
    }

    public void Dispose()
    {
        _signalRNotificationService.HandleNotificationReceived -= HandleSignalRNotificationReceived;
        _eventNotificationService.EventReceived -= HandleEventReceived;
    }

    Task HandleSignalRNotificationReceived(PokerAttackNotification notification)
    {
        switch (notification.NotificationType)
        {
            case PokerAttackNotificationType.GameStateChanged:
                GameState = JsonSerializer.Deserialize<GameStates>(notification.Payload!, JsonOptions.Get());
                break;
        }
        return Task.CompletedTask;
    }

    async Task HandleEventReceived(object sender, IEventArgs args)
    {
        switch (args)
        {
            case GameTransitionEventArgs gameTransitionEventArgs:
                await _signalRNotificationService.TransitionGameStateAsync(gameTransitionEventArgs.Data.GameId, gameTransitionEventArgs.Data.GameEvent);
                break;
        }
        await Task.CompletedTask;
    }
}
