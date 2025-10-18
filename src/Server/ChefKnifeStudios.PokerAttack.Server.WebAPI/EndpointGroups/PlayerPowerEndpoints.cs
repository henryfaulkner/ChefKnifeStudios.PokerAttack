using Ardalis.Result;
using ChefKnifeStudios.PokerAttack.Server.BL.Services;
using ChefKnifeStudios.PokerAttack.Shared.DTOs.Gameplay;

using Endpoints = ChefKnifeStudios.PokerAttack.Shared.PokerAttackApiEndpoints.PlayerPower;

namespace ChefKnifeStudios.PokerAttack.Server.WebAPI.EndpointGroups;

public static class PlayerPowerEndpoints
{
    public static IEndpointRouteBuilder MapPlayerPowerEndpoints(this IEndpointRouteBuilder builder)
    {
        var group = builder
            .MapGroup(string.Empty)
            .WithName("Player Powers")
            .WithTags("Player Powers");

        group.MapGet(Endpoints.GetSomePowers, (
            IPlayerPowerService playerPowerService,
            int count) =>
        {
            var result = playerPowerService.GetSomePowers(count);
            return Result.Success(result);
        })
        .WithName(nameof(Endpoints.GetSomePowers))
        .Produces<RoundDTO?>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status500InternalServerError);

        group.MapGet(Endpoints.SelectPlayerPower, async (
            IPlayerPowerService playerPowerService,
            string playerId,
            string powerId,
            CancellationToken cancellationToken = default) =>
        {
            await playerPowerService.SelectPlayerPowerAsync(playerId, powerId, cancellationToken);
            return Result.Success();
        })
        .WithName(nameof(Endpoints.SelectPlayerPower))
        .Produces<RoundDTO?>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status500InternalServerError);

        return builder;
    }
}
