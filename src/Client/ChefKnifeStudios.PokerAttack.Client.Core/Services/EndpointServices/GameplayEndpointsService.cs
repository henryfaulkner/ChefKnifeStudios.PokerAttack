using Ardalis.Result;
using ChefKnifeStudios.PokerAttack.Client.Core.Enums;
using ChefKnifeStudios.PokerAttack.Client.Core.Extensions;
using ChefKnifeStudios.PokerAttack.Shared.DTOs.Gameplay;
using Microsoft.Extensions.Logging;

using Endpoints = ChefKnifeStudios.PokerAttack.Shared.PokerAttackApiEndpoints.Gameplay;

namespace ChefKnifeStudios.PokerAttack.Client.Core.Services.EndpointServices;

public interface IGameplayEndpointsService
{
    Task<Result<IEnumerable<PlayerScoreDTO>?>> GetPlayerScores(string gameId, CancellationToken cancellationToken = default);
}

public class GameplayEndpointsService : IGameplayEndpointsService
{
    readonly ILogger<GameplayEndpointsService> _logger;
    readonly IHttpService _httpService;

    public GameplayEndpointsService(
        ILogger<GameplayEndpointsService> logger,
        IHttpServiceFactory httpServiceFactory)
    {
        _logger = logger;
        _httpService = httpServiceFactory.Create(nameof(APIs.PokerAttackAPI));
    }

    public async Task<Result<IEnumerable<PlayerScoreDTO>?>> GetPlayerScores(string gameId, CancellationToken cancellationToken = default)
    {
        try
        {
            var res = await _httpService.GetAsync<IEnumerable<PlayerScoreDTO>?>(
                Endpoints.GetPlayerScores.FormatRoute(gameId),
                cancellationToken
            );
            return res.LogErrors(_logger, "Gameplay GetPlayerScores call");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "An error occured.");
            return Result.Error();
        }
    }
}
