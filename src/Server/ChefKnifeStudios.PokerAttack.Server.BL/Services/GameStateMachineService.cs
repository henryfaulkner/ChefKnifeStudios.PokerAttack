using Ardalis.Result;
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
    Task<Result> TransitionAsync(string gameId, GameEvents gameEvent, CancellationToken cancellationToken = default);
}

public class GameStateMachineService(
    ILogger<GameStateMachineService> logger,
    IKeyValueRepository<GameStates> gameStateRepository,
    IKeyValueRepository<GameModes> gameModeRepository,
    IPokerAttackNotificationHelper notificationHelper,
    IEventNotificationService eventNotificationService) : IGameStateMachineService
{
    public async Task<Result> TransitionAsync(string gameId, GameEvents gameEvent, CancellationToken cancellationToken = default)
    {
        // Get game state
        var gameStateResult = await gameStateRepository.GetAsync(gameId, cancellationToken);
        if (!gameStateResult.IsSuccess || gameStateResult.Value == null)
            return Result.NotFound($"Game State not found. GameId {gameId}");

        var gameState = gameStateResult.Value;

        // Get game mode
        var gameModeResult = await gameModeRepository.GetAsync(gameId, cancellationToken);
        if (!gameModeResult.IsSuccess || gameModeResult.Value == null)
            return Result.NotFound($"Game Mode not found. GameId {gameId}");

        var gameMode = gameModeResult.Value;

        // Get transition
        var transition = GameTransitions.Get(gameState, gameEvent, gameMode);
        if (transition is not GameTransition)
        {
            logger.LogWarning("GameTransition does not exist. GameId: {GameId}, GameState: {GameState}, GameEvent: {GameEvent}",
                gameId, gameState, gameEvent);
            return Result.Invalid(new ValidationError($"No valid transition from {gameState} with event {gameEvent}"));
        }

        // Update state
        var updateResult = await gameStateRepository.UpdateAsync(gameId, transition.NextState, cancellationToken);
        if (!updateResult.IsSuccess)
            return updateResult;

        // Log successful transition
        logger.LogInformation(
            "Game state transition: GameId={GameId}, From={FromState}, To={ToState}, Event={GameEvent}",
            gameId, gameState, transition.NextState, gameEvent);

        // Post event
        try
        {
            eventNotificationService.PostEvent(this, new GameStateChangedEventArgs(gameId, transition.NextState));
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Subscriber threw in GameStateChanged handler.");
        }

        // Broadcast to clients
        await notificationHelper.BroadcastToGameAsync(gameId, new PokerAttackNotification
        (
            PokerAttackNotificationType.GameStateChanged,
            JsonSerializer.Serialize(transition.NextState, JsonOptions.Get())
        ));

        return Result.Success();
    }
}
