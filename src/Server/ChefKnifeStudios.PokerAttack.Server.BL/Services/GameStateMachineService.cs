using ChefKnifeStudios.PokerAttack.Server.Core.Interfaces;
using ChefKnifeStudios.PokerAttack.Server.Core.Interfaces.Repos;
using ChefKnifeStudios.PokerAttack.Server.Core.Models;
using ChefKnifeStudios.PokerAttack.Server.Data.Models;
using ChefKnifeStudios.PokerAttack.Server.Data.Repos;
using ChefKnifeStudios.PokerAttack.Server.Data.Specifications;
using ChefKnifeStudios.PokerAttack.Shared;
using ChefKnifeStudios.PokerAttack.Shared.DTOs.SignalR;
using ChefKnifeStudios.PokerAttack.Shared.Enums;
using Microsoft.Extensions.Logging;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace ChefKnifeStudios.PokerAttack.Server.BL.Services;

public interface IGameStateMachineService
{
    Task<GameStates> GetGameStateAsync(string gameId, CancellationToken cancellationToken = default); 
    Task TransitionAsync(string gameId, GameEvents gameEvent, CancellationToken cancellationToken = default);
    Task TransitionAsync(string hostPlayerClientId, string gameId, GameEvents gameEvent, CancellationToken cancellationToken = default);
}

public class GameStateMachineService(
    ILogger<GameStateMachineService> logger,
    IGameStateRepository gameStateRepository,
    IRepository<Game> gameRepository,
    IPokerAttackNotificationHelper notificationHelper) : IGameStateMachineService
{
    public async Task<GameStates> GetGameStateAsync(string gameId, CancellationToken cancellationToken = default)
    {
        var result = await gameStateRepository.GetAsync(gameId, cancellationToken)
            ?? throw new ApplicationException($"Game State not found. GameId {gameId}");
        return result;
    }

    public async Task TransitionAsync(string gameId, GameEvents gameEvent, CancellationToken cancellationToken = default)
    {
        var gameState = await gameStateRepository.GetAsync(gameId, cancellationToken)
            ?? throw new ApplicationException($"Game State not found. GameId {gameId}");
        var transition = GameTransitions.Get(gameState, gameEvent);
        if (transition is not GameTransition)
        {
            logger.LogWarning("GameTransition does not exist. GameState: {0}. GameEvent: {1}.", gameId, gameEvent);
            return;
        }
        await gameStateRepository.UpdateAsync(gameId, transition.NextState, cancellationToken);
        
        await notificationHelper.BroadcastToGameAsync(gameId, new PokerAttackNotification
        (
            PokerAttackNotificationType.GameStateChanged,
            JsonSerializer.Serialize(transition.NextState, JsonOptions.Get())
        ));
    }

    public async Task TransitionAsync(string hostPlayerClientId, string gameClientId, GameEvents gameEvent, CancellationToken cancellationToken = default)
    {
        var game = await gameRepository.FirstOrDefaultAsync(new GetGameByClientIdSpec(gameClientId), cancellationToken)
            ?? throw new ApplicationException($"Game State not found. GameId {gameClientId}");
        if (game.HostPlayerClientId != hostPlayerClientId) return;
        await TransitionAsync(gameClientId, gameEvent, cancellationToken);
    }
}
