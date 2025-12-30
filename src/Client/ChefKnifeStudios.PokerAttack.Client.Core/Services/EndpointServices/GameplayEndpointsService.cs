using Ardalis.Result;
using ChefKnifeStudios.PokerAttack.Client.Core.Enums;
using ChefKnifeStudios.PokerAttack.Client.Core.Extensions;
using ChefKnifeStudios.PokerAttack.Shared.DTOs.Gameplay;
using Microsoft.Extensions.Logging;

using Endpoints = ChefKnifeStudios.PokerAttack.Shared.PokerAttackApiEndpoints.Gameplay;

namespace ChefKnifeStudios.PokerAttack.Client.Core.Services.EndpointServices;

public interface IGameplayEndpointsService
{
    Task<Result<RoundDTO?>> GetLatestRoundAsync(string gameId, CancellationToken cancellationToken = default);
    Task<Result<int?>> GetPlayerWalletAsync(string gameId, string playerId, CancellationToken cancellationToken = default);
    Task<Result<PlayerStateDTO?>> GetPlayerStateAsync(string playerId, CancellationToken cancellationToken = default);
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

    public async Task<Result<RoundDTO?>> GetLatestRoundAsync(string gameId, CancellationToken cancellationToken = default)
    {
        try
        {
            var res = await _httpService.GetAsync<RoundDTO?>(
                Endpoints.GetLatestRound.FormatRoute(gameId),
                cancellationToken
            );
            return res.LogErrors(_logger, $"Gameplay GetLatestRound call. Lobby Id {gameId}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "An error occured.");
            return Result.Error();
        }
    }

    public async Task<Result<int?>> GetPlayerWalletAsync(string gameId, string playerId, CancellationToken cancellationToken = default)
    {
        try
        {
            var res = await _httpService.GetAsync<int?>(
                Endpoints.GetPlayerWallet.FormatRoute(gameId).FormatRoute(playerId),
                cancellationToken
            );
            return res.LogErrors(_logger, $"Gameplay GetLatestRound call. Lobby Id {gameId}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "An error occured.");
            return Result.Error();
        }
    }

    public async Task<Result<PlayerStateDTO?>> GetPlayerStateAsync(string playerId, CancellationToken cancellationToken = default)
    {
        try
        {
            var res = await _httpService.GetAsync<PlayerStateDTO?>(
                Endpoints.GetPlayerState.FormatRoute(playerId),
                cancellationToken
            );
            return res.LogErrors(_logger, $"Gameplay GetPlayerState call. Player Id {playerId}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "An error occured.");
            return Result.Error();
        }
    }
}
