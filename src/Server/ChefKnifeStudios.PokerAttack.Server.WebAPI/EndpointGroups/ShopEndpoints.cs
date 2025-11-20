using Ardalis.Result;
using ChefKnifeStudios.PokerAttack.Server.BL.Services;

using Endpoints = ChefKnifeStudios.PokerAttack.Shared.PokerAttackApiEndpoints.Shop;

namespace ChefKnifeStudios.PokerAttack.Server.WebAPI.EndpointGroups;

public class ShopItemDTO { }

public static class ShopEndpoints
{
    public static IEndpointRouteBuilder MapShopEndpoints(this IEndpointRouteBuilder builder)
    {
        var group = builder
            .MapGroup(string.Empty)
            .WithName("Shop")
            .WithTags("Shop");

        group.MapGet(Endpoints.GetShopItems, async (
            IGameService gameService,
            string gameId,
            string count,
            CancellationToken cancellationToken = default) =>
        {
            
        })
        .WithName(nameof(Endpoints.GetShopItems))
        .Produces<ShopItemDTO?>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status500InternalServerError);

        return builder;
    }
}
