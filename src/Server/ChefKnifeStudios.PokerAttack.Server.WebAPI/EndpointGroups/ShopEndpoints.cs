using Ardalis.Result;
using ChefKnifeStudios.PokerAttack.Server.BL.Services;
using ChefKnifeStudios.PokerAttack.Server.Core.Interfaces;
using ChefKnifeStudios.PokerAttack.Server.Core.Models;
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
            IKeyValueRepository<ActiveGame> activeGameRepository,
            string gameId,
            CancellationToken cancellationToken = default) =>
        {
            var items = itemRepository.GetRandomNumber();

            // Get the active game to calculate round-based pricing
            var activeGame = await activeGameRepository.GetAsync(gameId, cancellationToken);
            if (activeGame == null)
            {
                return Result.NotFound($"Active game not found with ID: {gameId}");
            }

            // Calculate adjusted price for each item: 15% increase per round
            var shopItems = items.Select(item => new ShopItemDTO
            {
                Item = item,
                AdjustedPrice = (int)(item.Price * (1 + 0.15 * activeGame.RoundNumber))
            });

            return Result.Success(shopItems);
        })
        .WithName(nameof(Endpoints.GetShopItems))
        .Produces<IEnumerable<ShopItemDTO>>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status500InternalServerError);

        group.MapGet(Endpoints.PurchaseShopItem, async (
            IShopService shopService,
            string gameId,
            string playerId,
            string shopItemId,
            CancellationToken cancellationToken = default) =>
        {
            var shopItem = await shopService.PurchaseItemAsync(gameId, playerId, shopItemId, cancellationToken);
            return Result.Success(shopItem);
        })
        .WithName(nameof(Endpoints.PurchaseShopItem))
        .Produces<ShopItemDTO>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status500InternalServerError);

        return builder;
    }
}
