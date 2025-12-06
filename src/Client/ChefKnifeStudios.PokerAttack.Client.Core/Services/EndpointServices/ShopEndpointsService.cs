using Ardalis.Result;
using ChefKnifeStudios.PokerAttack.Client.Core.Enums;
using ChefKnifeStudios.PokerAttack.Shared;
using ChefKnifeStudios.PokerAttack.Client.Core.Extensions;
using Microsoft.Extensions.Logging;

using Endpoints = ChefKnifeStudios.PokerAttack.Shared.PokerAttackApiEndpoints.Shop;

namespace ChefKnifeStudios.PokerAttack.Client.Core.Services.EndpointServices;

public interface IShopEndpointsService
{
    Task<Result<IEnumerable<ItemBase>>> GetShopItemsAsync(CancellationToken cancellationToken = default);
    Task<Result<ItemBase>> PurchaseShopItemAsync(string gameId, string playerId, string shopItemId, CancellationToken cancellationToken = default);
}

public class ShopEndpointsService : IShopEndpointsService
{
    readonly ILogger<ShopEndpointsService> _logger;
    readonly IHttpService _httpService;

    public ShopEndpointsService(
        ILogger<ShopEndpointsService> logger,
        IHttpServiceFactory httpServiceFactory)
    {
        _logger = logger;
        _httpService = httpServiceFactory.Create(nameof(APIs.PokerAttackAPI));
    }

    public async Task<Result<IEnumerable<ItemBase>>> GetShopItemsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var res = await _httpService.GetAsync<IEnumerable<ItemBase>>(
                Endpoints.GetShopItems,
                cancellationToken
            );
            return res.LogErrors(_logger, $"Shop GetShopItemsAsync call.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "An error occured.");
            return Result.Error();
        }
    }

    public async Task<Result<ItemBase>> PurchaseShopItemAsync(string gameId, string playerId, string shopItemId, CancellationToken cancellationToken = default)
    {
        try
        {
            var res = await _httpService.GetAsync<ItemBase>(
                Endpoints.PurchaseShopItem.FormatRoute(gameId).FormatRoute(playerId).FormatRoute(shopItemId),
                cancellationToken
            );
            return res.LogErrors(_logger, $"Shop PurchaseShopItemAsync call.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "An error occured.");
            return Result.Error();
        }
    }
}
