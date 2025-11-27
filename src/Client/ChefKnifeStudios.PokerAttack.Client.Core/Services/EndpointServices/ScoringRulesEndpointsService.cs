using Ardalis.Result;
using ChefKnifeStudios.PokerAttack.Client.Core.Enums;
using ChefKnifeStudios.PokerAttack.Client.Core.Extensions;
using ChefKnifeStudios.PokerAttack.Shared.DTOs;
using Microsoft.Extensions.Logging;

using Endpoints = ChefKnifeStudios.PokerAttack.Shared.PokerAttackApiEndpoints.ScoringRules;

namespace ChefKnifeStudios.PokerAttack.Client.Core.Services.EndpointServices;

public interface IScoringRulesEndpointsService
{
    Task<Result<ScoringRules>> GetScoringRulesAsync(CancellationToken cancellationToken = default);
}

public class ScoringRulesEndpointsService : IScoringRulesEndpointsService
{
    readonly ILogger<PlayerPowerEndpointsService> _logger;
    readonly IHttpService _httpService;

    public ScoringRulesEndpointsService(
        ILogger<PlayerPowerEndpointsService> logger,
        IHttpServiceFactory httpServiceFactory)
    {
        _logger = logger;
        _httpService = httpServiceFactory.Create(nameof(APIs.PokerAttackAPI));
    }

    public async Task<Result<ScoringRules>> GetScoringRulesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var res = await _httpService.GetAsync<ScoringRules>(
                Endpoints.GetScoringRules,
                cancellationToken
            );
            return res.LogErrors(_logger, $"ScoringRules GetScoringRulesAsync call.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "An error occured.");
            return Result.Error();
        }
    }
}
