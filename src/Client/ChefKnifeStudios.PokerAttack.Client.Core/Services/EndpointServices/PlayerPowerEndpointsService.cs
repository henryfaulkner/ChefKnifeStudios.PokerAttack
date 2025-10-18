using Ardalis.Result;
using ChefKnifeStudios.PokerAttack.Client.Core.Enums;
using ChefKnifeStudios.PokerAttack.Client.Core.Extensions;
using ChefKnifeStudios.PokerAttack.Shared.DTOs;
using Microsoft.Extensions.Logging;

using Endpoints = ChefKnifeStudios.PokerAttack.Shared.PokerAttackApiEndpoints.PlayerPower;

namespace ChefKnifeStudios.PokerAttack.Client.Core.Services.EndpointServices;

public interface IPlayerPowerEndpointsService
{
    Task<Result<IEnumerable<PlayerPowerDTO>>> GetSomePowersAsync(CancellationToken cancellationToken = default);
    Task<Result<Discard>> SelectPlayerPowerAsync(string playerId, string powerId, CancellationToken cancellationToken = default);
}

public class PlayerPowerEndpointsService : IPlayerPowerEndpointsService
{
    readonly ILogger<PlayerPowerEndpointsService> _logger;
    readonly IHttpService _httpService;

    public PlayerPowerEndpointsService(
        ILogger<PlayerPowerEndpointsService> logger,
        IHttpServiceFactory httpServiceFactory)
    {
        _logger = logger;
        _httpService = httpServiceFactory.Create(nameof(APIs.PokerAttackAPI));
    }

    public async Task<Result<IEnumerable<PlayerPowerDTO>>> GetSomePowersAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var res = await _httpService.GetAsync<IEnumerable<PlayerPowerDTO>>(
                Endpoints.GetSomePowers,
                cancellationToken
            );
            return res.LogErrors(_logger, $"PlayerPower GetSomePowersAsync call.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "An error occured.");
            return Result.Error();
        }
    }

    public async Task<Result<Discard>> SelectPlayerPowerAsync(string playerId, string powerId, CancellationToken cancellationToken = default)
    {
        try
        {
            var res = await _httpService.GetAsync<Discard>(
                Endpoints.SelectPlayerPower.FormatRoute(playerId).FormatRoute(powerId),
                cancellationToken
            );
            return res.LogErrors(_logger, $"PlayerPower SelectPlayerPowerAsync call.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "An error occured.");
            return Result.Error();
        }
    }
}
