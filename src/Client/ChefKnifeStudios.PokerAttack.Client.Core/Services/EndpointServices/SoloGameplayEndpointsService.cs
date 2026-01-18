using Ardalis.Result;
using ChefKnifeStudios.PokerAttack.Client.Core.Enums;
using ChefKnifeStudios.PokerAttack.Client.Core.Extensions;
using ChefKnifeStudios.PokerAttack.Shared.DTOs.SoloGameplay;
using Microsoft.Extensions.Logging;

using Endpoints = ChefKnifeStudios.PokerAttack.Shared.PokerAttackApiEndpoints.SoloGameplay;

namespace ChefKnifeStudios.PokerAttack.Client.Core.Services.EndpointServices;

public interface ISoloGameplayEndpointsService
{
    Task<Result<StartSoloGameResDTO?>> StartGameAsync(StartSoloGameReqDTO request, CancellationToken cancellationToken = default);
    Task<Result<PlayHandResDTO?>> PlayHandAsync(string gameId, PlayHandReqDTO request, CancellationToken cancellationToken = default);
    Task<Result<DiscardResDTO?>> DiscardAsync(string gameId, DiscardReqDTO request, CancellationToken cancellationToken = default);
    Task<Result> EndRoundAsync(string gameId, CancellationToken cancellationToken = default);
    Task<Result<SoloGameStateDTO?>> GetGameStateAsync(string gameId, CancellationToken cancellationToken = default);
    Task<Result<PurchaseItemResDTO?>> PurchaseShopItemAsync(string gameId, string shopItemId, CancellationToken cancellationToken = default);
    Task<Result<AdvancePhaseResDTO?>> AdvancePhaseAsync(string gameId, CancellationToken cancellationToken = default);
}

public class SoloGameplayEndpointsService : ISoloGameplayEndpointsService
{
    readonly ILogger<SoloGameplayEndpointsService> _logger;
    readonly IHttpService _httpService;

    public SoloGameplayEndpointsService(
        ILogger<SoloGameplayEndpointsService> logger,
        IHttpServiceFactory httpServiceFactory)
    {
        _logger = logger;
        _httpService = httpServiceFactory.Create(nameof(APIs.PokerAttackAPI));
    }

    public async Task<Result<StartSoloGameResDTO?>> StartGameAsync(StartSoloGameReqDTO request, CancellationToken cancellationToken = default)
    {
        try
        {
            var res = await _httpService.PostAsync<StartSoloGameReqDTO, StartSoloGameResDTO?>(
                Endpoints.StartGame,
                request,
                cancellationToken
            );
            return res.LogErrors(_logger, "SoloGameplay StartGameAsync call");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "An error occured.");
            return Result.Error();
        }
    }

    public async Task<Result<PlayHandResDTO?>> PlayHandAsync(string gameId, PlayHandReqDTO request, CancellationToken cancellationToken = default)
    {
        try
        {
            var res = await _httpService.PostAsync<PlayHandReqDTO, PlayHandResDTO?>(
                Endpoints.PlayHand.FormatRoute(gameId),
                request,
                cancellationToken
            );
            return res.LogErrors(_logger, $"SoloGameplay PlayHandAsync call. Game Id {gameId}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "An error occured.");
            return Result.Error();
        }
    }

    public async Task<Result<DiscardResDTO?>> DiscardAsync(string gameId, DiscardReqDTO request, CancellationToken cancellationToken = default)
    {
        try
        {
            var res = await _httpService.PostAsync<DiscardReqDTO, DiscardResDTO?>(
                Endpoints.Discard.FormatRoute(gameId),
                request,
                cancellationToken
            );
            return res.LogErrors(_logger, $"SoloGameplay DiscardAsync call. Game Id {gameId}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "An error occured.");
            return Result.Error();
        }
    }

    public async Task<Result> EndRoundAsync(string gameId, CancellationToken cancellationToken = default)
    {
        try
        {
            var res = await _httpService.PostAsync<object, object?>(
                Endpoints.EndRound.FormatRoute(gameId),
                new { },
                cancellationToken
            );
            return res.IsSuccess ? Result.Success() : Result.Error();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "An error occured.");
            return Result.Error();
        }
    }

    public async Task<Result<SoloGameStateDTO?>> GetGameStateAsync(string gameId, CancellationToken cancellationToken = default)
    {
        try
        {
            var res = await _httpService.GetAsync<SoloGameStateDTO?>(
                Endpoints.GetGameState.FormatRoute(gameId),
                cancellationToken
            );
            return res.LogErrors(_logger, $"SoloGameplay GetGameStateAsync call. Game Id {gameId}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "An error occured.");
            return Result.Error();
        }
    }

    public async Task<Result<PurchaseItemResDTO?>> PurchaseShopItemAsync(string gameId, string shopItemId, CancellationToken cancellationToken = default)
    {
        try
        {
            var res = await _httpService.PostAsync<object, PurchaseItemResDTO?>(
                Endpoints.PurchaseShopItem.FormatRoute(gameId).FormatRoute(shopItemId),
                new { },
                cancellationToken
            );
            return res.LogErrors(_logger, $"SoloGameplay PurchaseShopItemAsync call. Game Id {gameId}, Item Id {shopItemId}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "An error occured.");
            return Result.Error();
        }
    }

    public async Task<Result<AdvancePhaseResDTO?>> AdvancePhaseAsync(string gameId, CancellationToken cancellationToken = default)
    {
        try
        {
            var res = await _httpService.PostAsync<object, AdvancePhaseResDTO?>(
                Endpoints.AdvancePhase.FormatRoute(gameId),
                new { },
                cancellationToken
            );
            return res.LogErrors(_logger, $"SoloGameplay AdvancePhaseAsync call. Game Id {gameId}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "An error occured.");
            return Result.Error();
        }
    }
}
