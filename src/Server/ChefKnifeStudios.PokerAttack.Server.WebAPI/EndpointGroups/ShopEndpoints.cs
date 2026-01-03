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
            var itemsResult = itemRepository.GetRandomNumber();
            if (!itemsResult.IsSuccess)
                return Result.Error("Failed to retrieve shop items.");

            var activeGameResult = await activeGameRepository.GetAsync(gameId, cancellationToken);
            if (!activeGameResult.IsSuccess || activeGameResult.Value is null)
                return Result.NotFound($"Active game not found with ID: {gameId}");

            var activeGame = activeGameResult.Value;

            // Calculate adjusted price for each item: 15% increase per round
            var shopItems = itemsResult.Value.Select(item => new ShopItemDTO
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
            return await shopService.PurchaseItemAsync(gameId, playerId, shopItemId, cancellationToken);
        })
        .WithName(nameof(Endpoints.PurchaseShopItem))
        .Produces<ShopItemDTO>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status500InternalServerError);

        return builder;
    }
}
