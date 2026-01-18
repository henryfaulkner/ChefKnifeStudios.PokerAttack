using ChefKnifeStudios.PokerAttack.Server.BL.Services;
using ChefKnifeStudios.PokerAttack.Shared.DTOs.SoloGameplay;

using Endpoints = ChefKnifeStudios.PokerAttack.Shared.PokerAttackApiEndpoints.SoloGameplay;

namespace ChefKnifeStudios.PokerAttack.Server.WebAPI.EndpointGroups;

public static class SoloGameplayEndpoints
{
    const string Prefix = "Solo";

    public static IEndpointRouteBuilder MapSoloGameplayEndpoints(this IEndpointRouteBuilder builder)
    {
        var group = builder
            .MapGroup(string.Empty)
            .WithName("SoloGameplay")
            .WithTags("SoloGameplay");

        group.MapPost(Endpoints.StartGame, async (
            StartSoloGameReqDTO reqBody,
            ISoloGameplayService soloGameplayService,
            CancellationToken cancellationToken = default) =>
        {
            return await soloGameplayService.StartGameAsync(reqBody, cancellationToken);
        })
        .WithName($"{Prefix}{nameof(Endpoints.StartGame)}")
        .Accepts<StartSoloGameReqDTO>("application/json")
        .Produces<StartSoloGameResDTO>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status500InternalServerError);

        group.MapPost(Endpoints.PlayHand, async (
            string gameId,
            PlayHandReqDTO reqBody,
            ISoloGameplayService soloGameplayService,
            CancellationToken cancellationToken = default) =>
        {
            return await soloGameplayService.PlayHandAsync(gameId, reqBody, cancellationToken);
        })
        .WithName($"{Prefix}{nameof(Endpoints.PlayHand)}")
        .Accepts<PlayHandReqDTO>("application/json")
        .Produces<PlayHandResDTO>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status500InternalServerError);

        group.MapPost(Endpoints.Discard, async (
            string gameId,
            DiscardReqDTO reqBody,
            ISoloGameplayService soloGameplayService,
            CancellationToken cancellationToken = default) =>
        {
            return await soloGameplayService.DiscardAsync(gameId, reqBody, cancellationToken);
        })
        .WithName($"{Prefix}{nameof(Endpoints.Discard)}")
        .Accepts<DiscardReqDTO>("application/json")
        .Produces<DiscardResDTO>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status500InternalServerError);

        group.MapGet(Endpoints.GetGameState, async (
            string gameId,
            ISoloGameplayService soloGameplayService,
            CancellationToken cancellationToken = default) =>
        {
            return await soloGameplayService.GetGameStateAsync(gameId, cancellationToken);
        })
        .WithName($"{Prefix}{nameof(Endpoints.GetGameState)}")
        .Produces<SoloGameStateDTO>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status404NotFound)
        .Produces(StatusCodes.Status500InternalServerError);

        group.MapPost(Endpoints.PurchaseShopItem, async (
            string gameId,
            string shopItemId,
            ISoloGameplayService soloGameplayService,
            CancellationToken cancellationToken = default) =>
        {
            return await soloGameplayService.PurchaseShopItemAsync(gameId, shopItemId, cancellationToken);
        })
        .WithName($"{Prefix}{nameof(Endpoints.PurchaseShopItem)}")
        .Produces<PurchaseItemResDTO>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status500InternalServerError);

        group.MapPost(Endpoints.AdvancePhase, async (
            string gameId,
            ISoloGameplayService soloGameplayService,
            CancellationToken cancellationToken = default) =>
        {
            return await soloGameplayService.AdvancePhaseAsync(gameId, cancellationToken);
        })
        .WithName($"{Prefix}{nameof(Endpoints.AdvancePhase)}")
        .Produces<AdvancePhaseResDTO>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status500InternalServerError);

        return builder;
    }
}
