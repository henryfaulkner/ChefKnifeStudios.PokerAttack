using ChefKnifeStudios.PokerAttack.Server.Core.Interfaces;
using ChefKnifeStudios.PokerAttack.Server.Core.Models;
using ChefKnifeStudios.PokerAttack.Shared;
using ChefKnifeStudios.PokerAttack.Shared.DTOs.SignalR;
using ChefKnifeStudios.PokerAttack.Shared.Enums;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace ChefKnifeStudios.PokerAttack.Server.BL.Services;

public sealed class GameStateChangedEventArgs : EventArgs, IEventArgs
{
    public string GameId { get; }
    public GameStates NewState { get; }

    public GameStateChangedEventArgs(string gameId, GameStates newState)
    {
        GameId = gameId;
        NewState = newState;
    }
}

public interface IGameStateMachineService
{
    Task TransitionAsync(string gameId, GameEvents gameEvent, CancellationToken cancellationToken = default);
}

public class GameStateMachineService(
    ILogger<GameStateMachineService> logger,
    IKeyValueRepository<GameStates> gameStateRepository,
    IKeyValueRepository<GameModes> gameModeRepository,
    IPokerAttackNotificationHelper notificationHelper,
    IEventNotificationService eventNotificationService) : IGameStateMachineService
{
    public async Task TransitionAsync(string gameId, GameEvents gameEvent, CancellationToken cancellationToken = default)
    {
        var gameState = await gameStateRepository.GetAsync(gameId, cancellationToken) is GameStates gs
            ? gs
            : throw new ApplicationException($"Game State not found. GameId {gameId}");
        var gameMode = await gameModeRepository.GetAsync(gameId, cancellationToken) is GameModes gm
            ? gm
            : throw new ApplicationException($"Game Mode not found. GameId {gameId}");
        var transition = GameTransitions.Get(gameState, gameEvent, gameMode);
        if (transition is not GameTransition)
        {
            logger.LogWarning("GameTransition does not exist. GameState: {0}. GameEvent: {1}.", gameId, gameEvent);
            return;
        }
        await gameStateRepository.UpdateAsync(gameId, transition.NextState, cancellationToken);

        try
        {
            eventNotificationService.PostEvent(this, new GameStateChangedEventArgs(gameId, transition.NextState));
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Subscriber threw in GameStateChanged handler.");
        }

        await notificationHelper.BroadcastToGameAsync(gameId, new PokerAttackNotification
        (
            PokerAttackNotificationType.GameStateChanged,
            JsonSerializer.Serialize(transition.NextState, JsonOptions.Get())
        ));
    }
}
