using Ardalis.Result;
using ChefKnifeStudios.PokerAttack.Server.Core.Interfaces;
using ChefKnifeStudios.PokerAttack.Server.Core.Models;
using ChefKnifeStudios.PokerAttack.Shared.DTOs.SignalR;
using ChefKnifeStudios.PokerAttack.Shared.DTOs.Tests;
using ChefKnifeStudios.PokerAttack.Shared.Enums;
using Microsoft.AspNetCore.Mvc;

using Endpoints = ChefKnifeStudios.PokerAttack.Shared.PokerAttackApiEndpoints.Test;

namespace ChefKnifeStudios.PokerAttack.Server.WebAPI.EndpointGroups;

public static class TestEndpoints
{
    public static IEndpointRouteBuilder MapTestEndpoints(this IEndpointRouteBuilder builder)
    {
        var group = builder
            .MapGroup(string.Empty)
            .WithName("Test")
            .WithTags("Test");

        group.MapPost(Endpoints.SignalR, async (
            [FromBody] SignalRReq reqBody,
            [FromServices] IPokerAttackNotificationHelper signalRHelper,
            [FromServices] ILoggerFactory loggerFactory,
            HttpContext context,
            CancellationToken cancellationToken = default) =>
        {
            var logger = loggerFactory.CreateLogger(nameof(TestEndpoints));
            try
            {
                await signalRHelper.BroadcastToGameAsync(
                    reqBody.GameId, 
                    new PokerAttackNotification(PokerAttackNotificationType.Test, reqBody.Message),
                    cancellationToken
                );
                return Result.Success();
            }
            catch (ApplicationException ex)
            {
                logger.LogError(ex, "Exception in Test.SignalR endpoint. TraceIdentifier: {TraceId}", context.TraceIdentifier);
                return Result.Error("An unexpected error occurred.");
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Exception in Test.SignalR endpoint. TraceIdentifier: {TraceId}", context.TraceIdentifier);
                return Result.CriticalError("An unexpected error occurred.");
            }
        })
        .WithName(nameof(Endpoints.SignalR))
        .Produces<IEnumerable<string>>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status500InternalServerError);

        group.MapPost(Endpoints.Cleanup, async (
            [FromServices] IKeyValueRepository<Lobby> lobbyRepository,
            [FromServices] IKeyValueRepository<ActiveGame> activeGameRepository,
            [FromServices] IKeyValueRepository<GamePlayer> gamePlayerRepository,
            [FromServices] IKeyValueRepository<GameStates> gameStateRepository,
            [FromServices] IKeyValueRepository<GameModes> gameModeRepository,
            [FromServices] ILoggerFactory loggerFactory,
            HttpContext context,
            CancellationToken cancellationToken = default) =>
        {
            var logger = loggerFactory.CreateLogger(nameof(TestEndpoints));
            try
            {
                logger.LogInformation("Test cleanup endpoint called - clearing all repository data");

                // Get all data from each repository
                var lobbiesResult = await lobbyRepository.GetAllAsync(cancellationToken);
                if (!lobbiesResult.IsSuccess || lobbiesResult.Value is null)
                    return Result.Error("Failed to retrieve lobbies for cleanup.");

                var activeGamesResult = await activeGameRepository.GetAllAsync(cancellationToken);
                if (!activeGamesResult.IsSuccess || activeGamesResult.Value is null)
                    return Result.Error("Failed to retrieve active games for cleanup.");

                var gamePlayersResult = await gamePlayerRepository.GetAllAsync(cancellationToken);
                if (!gamePlayersResult.IsSuccess || gamePlayersResult.Value is null)
                    return Result.Error("Failed to retrieve game players for cleanup.");

                var gameStatesResult = await gameStateRepository.GetAllAsync(cancellationToken);
                if (!gameStatesResult.IsSuccess || gameStatesResult.Value is null)
                    return Result.Error("Failed to retrieve game states for cleanup.");

                var gameModesResult = await gameModeRepository.GetAllAsync(cancellationToken);
                if (!gameModesResult.IsSuccess || gameModesResult.Value is null)
                    return Result.Error("Failed to retrieve game modes for cleanup.");

                var lobbies = lobbiesResult.Value;
                var activeGames = activeGamesResult.Value;
                var gamePlayers = gamePlayersResult.Value;
                var gameStates = gameStatesResult.Value;
                var gameModes = gameModesResult.Value;

                // Delete all lobbies
                foreach (var (lobbyId, _) in lobbies)
                {
                    await lobbyRepository.DeleteAsync(lobbyId, cancellationToken);
                }

                // Delete all active games
                foreach (var (gameId, _) in activeGames)
                {
                    await activeGameRepository.DeleteAsync(gameId, cancellationToken);
                }

                // Delete all game players
                foreach (var (playerId, _) in gamePlayers)
                {
                    await gamePlayerRepository.DeleteAsync(playerId, cancellationToken);
                }

                // Delete all game states
                foreach (var (gameId, _) in gameStates)
                {
                    await gameStateRepository.DeleteAsync(gameId, cancellationToken);
                }

                // Delete all game modes
                foreach (var (gameId, _) in gameModes)
                {
                    await gameModeRepository.DeleteAsync(gameId, cancellationToken);
                }

                logger.LogInformation(
                    "Test cleanup complete - Deleted: {LobbyCount} lobbies, {GameCount} games, {PlayerCount} players, {StateCount} states, {ModeCount} modes",
                    lobbies.Count, activeGames.Count, gamePlayers.Count, gameStates.Count, gameModes.Count);

                return Result.Success();
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Exception in Test.Cleanup endpoint. TraceIdentifier: {TraceId}", context.TraceIdentifier);
                return Result.CriticalError("An unexpected error occurred during cleanup.");
            }
        })
        .WithName(nameof(Endpoints.Cleanup))
        .Produces(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status500InternalServerError);

        return builder;
    }
}
