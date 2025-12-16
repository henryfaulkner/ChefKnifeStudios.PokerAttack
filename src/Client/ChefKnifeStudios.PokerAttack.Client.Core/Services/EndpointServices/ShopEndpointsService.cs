using Ardalis.Result;
using ChefKnifeStudios.PokerAttack.Client.Core.Enums;
using ChefKnifeStudios.PokerAttack.Shared;
using ChefKnifeStudios.PokerAttack.Shared.DTOs;
using ChefKnifeStudios.PokerAttack.Client.Core.Extensions;
using Microsoft.Extensions.Logging;

using Endpoints = ChefKnifeStudios.PokerAttack.Shared.PokerAttackApiEndpoints.Shop;

namespace ChefKnifeStudios.PokerAttack.Client.Core.Services.EndpointServices;

public interface IShopEndpointsService
{
    Task<Result<IEnumerable<ShopItemDTO>>> GetShopItemsAsync(string gameId, CancellationToken cancellationToken = default);
    Task<Result<ShopItemDTO>> PurchaseShopItemAsync(string gameId, string playerId, string shopItemId, CancellationToken cancellationToken = default);
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

    public async Task<Result<IEnumerable<ShopItemDTO>>> GetShopItemsAsync(string gameId, CancellationToken cancellationToken = default)
    {
        try
        {
            var res = await _httpService.GetAsync<IEnumerable<ShopItemDTO>>(
                Endpoints.GetShopItems.FormatRoute(gameId),
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

    public async Task<Result<ShopItemDTO>> PurchaseShopItemAsync(string gameId, string playerId, string shopItemId, CancellationToken cancellationToken = default)
    {
        try
        {
            var res = await _httpService.GetAsync<ShopItemDTO>(
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
