using Ardalis.Result;
using ChefKnifeStudios.PokerAttack.Client.Core.Enums;
using ChefKnifeStudios.PokerAttack.Client.Core.Extensions;
using ChefKnifeStudios.PokerAttack.Shared.DTOs;
using Microsoft.Extensions.Logging;

using Endpoints = ChefKnifeStudios.PokerAttack.Shared.PokerAttackApiEndpoints.Settings;

namespace ChefKnifeStudios.PokerAttack.Client.Core.Services.EndpointServices;

public interface ISettingsEndpointsService
{
    Task<Result<GameSettingsDTO>> GetGameSettingsAsync(CancellationToken cancellationToken = default);
}

public class SettingsEndpointsService : ISettingsEndpointsService
{
    readonly ILogger<SettingsEndpointsService> _logger;
    readonly IHttpService _httpService;

    public SettingsEndpointsService(
        ILogger<SettingsEndpointsService> logger,
        IHttpServiceFactory httpServiceFactory)
    {
        _logger = logger;
        _httpService = httpServiceFactory.Create(nameof(APIs.PokerAttackAPI));
    }

    public async Task<Result<GameSettingsDTO>> GetGameSettingsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var res = await _httpService.GetAsync<GameSettingsDTO>(
                Endpoints.GetGameSettings,
                cancellationToken
            );
            return res.LogErrors(_logger, $"Settings GetGameSettingsAsync call.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "An error occured.");
            return Result.Error();
        }
    }
}
