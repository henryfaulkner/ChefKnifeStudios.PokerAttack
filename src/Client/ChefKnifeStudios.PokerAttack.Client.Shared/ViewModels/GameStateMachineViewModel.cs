using ChefKnifeStudios.PokerAttack.Client.Core.Services;
using ChefKnifeStudios.PokerAttack.Client.Shared.EventArgs;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;

namespace ChefKnifeStudios.PokerAttack.Client.Shared.ViewModels;

public enum GameStates
{
    InGame,
    Scoreboard,
    Elimination,
    Upgrade,
}

public enum GameEvents
{
    Next,
}

public record GameTransition(GameStates CurrState, GameEvents GameEvent, GameStates NextState);

public static class GameTransitions
{
    public static GameTransition? Get(GameStates currState, GameEvents gameEvent) =>
        Get().FirstOrDefault(x => x.CurrState == currState && x.GameEvent == gameEvent);

    public static GameTransition[] Get() =>
        new GameTransition[] 
        {
            new GameTransition(GameStates.InGame, GameEvents.Next, GameStates.Scoreboard),
            new GameTransition(GameStates.Scoreboard, GameEvents.Next, GameStates.Elimination),
            new GameTransition(GameStates.Elimination, GameEvents.Next, GameStates.Upgrade),
            new GameTransition(GameStates.Upgrade, GameEvents.Next, GameStates.InGame),
        };
}

public interface IGameStateMachineViewModel : IViewModel
{
    GameStates GameState { get; }
    void Transition(GameEvents gameEvent);
}

public partial class GameStateMachineViewModel : BaseViewModel, IGameStateMachineViewModel
{
    readonly ILogger<GameStateMachineViewModel> _logger;
    readonly IEventNotificationService _eventNotificationService;

    [ObservableProperty]
    public GameStates _gameState = GameStates.InGame;

    public GameStateMachineViewModel(
        ILogger<GameStateMachineViewModel> logger,
        IEventNotificationService eventNotificationService)
    {
        _logger = logger;
        _eventNotificationService = eventNotificationService;

        _eventNotificationService.EventReceived += HandleEventReceived;
    }

    public void Transition(GameEvents gameEvent)
    {
        var transition = GameTransitions.Get(GameState, gameEvent);
        if (transition is not GameTransition)
        {
            _logger.LogWarning("GameTransition does not exist. GameState: {0}. GameEvent: {1}.", GameState, gameEvent);
            return;
        }
        GameState = transition.NextState;
    }

    Task HandleEventReceived(object sender, IEventArgs args)
    {
        switch (args)
        {
            case GameTransitionEventArgs gameTransitionEventArgs:
                Transition(gameTransitionEventArgs.Data.GameEvent);
                break;
        }
        return Task.CompletedTask;
    }
}
