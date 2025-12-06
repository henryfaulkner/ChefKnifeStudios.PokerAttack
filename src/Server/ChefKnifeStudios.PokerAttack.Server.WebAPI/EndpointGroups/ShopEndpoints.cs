using Ardalis.Result;
using ChefKnifeStudios.PokerAttack.Server.BL.Services;
using ChefKnifeStudios.PokerAttack.Server.Core.Interfaces;
using ChefKnifeStudios.PokerAttack.Shared;
using ChefKnifeStudios.PokerAttack.Shared.DTOs;
using Endpoints = ChefKnifeStudios.PokerAttack.Shared.PokerAttackApiEndpoints.Shop;

namespace ChefKnifeStudios.PokerAttack.Server.WebAPI.EndpointGroups;

public static class ShopEndpoints
{
    public static IEndpointRouteBuilder MapShopEndpoints(this IEndpointRouteBuilder builder)
    {
        var group = builder
            .MapGroup(string.Empty)
            .WithName("Shop")
            .WithTags("Shop");

        group.MapGet(Endpoints.GetShopItems, async (
            IItemRepository itemRepository,
            CancellationToken cancellationToken = default) =>
        {
            var items = itemRepository.GetRandomNumber();
            return Result.Success(items);
        })
        .WithName(nameof(Endpoints.GetShopItems))
        .Produces<IEnumerable<ItemBase>>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status500InternalServerError);

        group.MapGet(Endpoints.PurchaseShopItem, async (
            IShopService shopService,
            string gameId,
            string playerId,
            string shopItemId,
            CancellationToken cancellationToken = default) =>
        {
            var item = await shopService.PurchaseItemAsync(gameId, playerId, shopItemId, cancellationToken);
            return Result.Success(item);
        })
        .WithName(nameof(Endpoints.PurchaseShopItem))
        .Produces<ItemBase>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status500InternalServerError);

        return builder;
    }
}
