using Ardalis.Result;
using ChefKnifeStudios.PokerAttack.Client.Core.Enums;
using ChefKnifeStudios.PokerAttack.Client.Core.Extensions;
using ChefKnifeStudios.PokerAttack.Shared.DTOs.Tests;
using Microsoft.Extensions.Logging;

using Endpoints = ChefKnifeStudios.PokerAttack.Shared.PokerAttackApiEndpoints;

namespace ChefKnifeStudios.PokerAttack.Client.Core.Services;

public interface ITestEndpointsService
{
    Task<Result<object>> SignalRAsync(SignalRReq reqBody, CancellationToken cancellationToken = default);
}

public class TestEndpointsService : ITestEndpointsService
{
    readonly ILogger<TestEndpointsService> _logger;
    readonly IHttpService _httpService;

    public TestEndpointsService(
        ILogger<TestEndpointsService> logger,
        IHttpServiceFactory httpClientFactory)
    {
        _logger = logger;
        _httpService = httpClientFactory.Create(nameof(APIs.PokerAttackAPI));
    }

    public async Task<Result<object>> SignalRAsync(SignalRReq reqBody, CancellationToken cancellationToken = default)
    {
        try
        {
            var res = await _httpService.PostAsync<SignalRReq, object>(
                Endpoints.Test.SignalR, 
                reqBody,
                cancellationToken
            );
            return res.LogErrors<object>(_logger, "SignalR call");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "An error occured.");
            return Result.Error();
        }
    }
}
