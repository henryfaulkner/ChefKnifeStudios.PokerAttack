using Ardalis.Result;
using ChefKnifeStudios.PokerAttack.Server.BL;
using ChefKnifeStudios.PokerAttack.Server.BL.Services;
using ChefKnifeStudios.PokerAttack.Shared.DTOs.Gameplay;

using Endpoints = ChefKnifeStudios.PokerAttack.Shared.PokerAttackApiEndpoints.Gameplay;

namespace ChefKnifeStudios.PokerAttack.Server.WebAPI.EndpointGroups;

public static class GameplayEndpoints
{
    public static IEndpointRouteBuilder MapGameplayEndpoints(this IEndpointRouteBuilder builder)
    {
        var group = builder
            .MapGroup(string.Empty)
            .WithName("Gameplay")
            .WithTags("Gameplay");

        group.MapGet(Endpoints.GetLatestRound, async (
            IGameService gameService,
            string gameId,
            CancellationToken cancellationToken = default) =>
        {
            return await gameService.GetLatestRoundFromGame(gameId, cancellationToken);
        })
        .WithName(nameof(Endpoints.GetLatestRound))
        .Produces<RoundDTO?>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status500InternalServerError);

        group.MapGet(Endpoints.GetPlayerWallet, async (
            IGameService gameService,
            string gameId,
            string playerId,
            CancellationToken cancellationToken = default) =>
        {
            return await gameService.GetPlayerWalletAsync(playerId, cancellationToken);
        })
        .WithName(nameof(Endpoints.GetPlayerWallet))
        .Produces<int?>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status500InternalServerError);

        group.MapGet(Endpoints.LeaveGame, async (
            IGameService gameService,
            string gameId,
            string playerId,
            CancellationToken cancellationToken = default) =>
        {
            return await gameService.LeaveGameAsync(gameId, playerId, cancellationToken);
        })
        .WithName(nameof(Endpoints.LeaveGame))
        .Produces<int?>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status500InternalServerError);

        group.MapGet(Endpoints.GetGameState, async (
            IGameResyncService gameResyncService,
            IPhaseReconcilerService phaseReconciler,
            ILogger<Program> logger,
            string gameId,
            string playerId,
            CancellationToken cancellationToken = default) =>
        {
            if (string.IsNullOrWhiteSpace(gameId) || string.IsNullOrWhiteSpace(playerId))
                return Result.Invalid(new ValidationError("gameId and playerId are required."));

            try
            {
                // Reconciliation trigger: a returning client should see the current phase, not
                // the one it left. Safe under concurrent calls — the reconciler's per-game lock
                // and cleared-deadline write ensure only one caller advances a given phase.
                await phaseReconciler.ReconcileGameAsync(gameId, cancellationToken);

                var result = await gameResyncService.GetGameStateAsync(gameId, playerId, cancellationToken);
                return result.ToMinimalApiResult();
            }
            catch (Exception ex)
            {
                // Anything that reaches here comes from the two calls in this try block, both
                // of which only touch the durable store — treat any failure as store
                // unavailability rather than trying to enumerate every wrapper exception type
                // EF Core's Npgsql provider might use for a connection failure. Result<T>'s
                // implicit IResult conversion does not reliably map ResultStatus.Unavailable to
                // an HTTP 503 in the installed Ardalis.Result version, so return the status
                // code directly rather than relying on that mapping.
                logger.LogError(ex, "Durable store unreachable during resync for game {GameId}", gameId);
                return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
            }
        })
        .WithName(nameof(Endpoints.GetGameState))
        .Produces<GameStateDTO>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status404NotFound)
        .Produces(StatusCodes.Status503ServiceUnavailable);

        return builder;
    }
}
