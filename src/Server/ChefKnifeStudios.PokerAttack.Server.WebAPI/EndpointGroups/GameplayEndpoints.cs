using Ardalis.Result;
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
            var round = await gameService.GetLatestRoundFromGame(gameId, cancellationToken);
            return Result.Success(round);
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
            var wallet = await gameService.GetPlayerScoreAsync(playerId, cancellationToken);
            return Result.Success(wallet);
        })
        .WithName(nameof(Endpoints.GetPlayerWallet))
        .Produces<int?>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status500InternalServerError);

        return builder;
    }
}
