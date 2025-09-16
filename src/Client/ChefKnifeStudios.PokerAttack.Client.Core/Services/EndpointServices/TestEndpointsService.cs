using Ardalis.Result;
using ChefKnifeStudios.PokerAttack.Client.Core.Enums;
using ChefKnifeStudios.PokerAttack.Client.Core.Extensions;
using ChefKnifeStudios.PokerAttack.Shared.DTOs.Tests;
using Microsoft.Extensions.Logging;

using Endpoints = ChefKnifeStudios.PokerAttack.Shared.PokerAttackApiEndpoints.Test;

namespace ChefKnifeStudios.PokerAttack.Client.Core.Services.EndpointServices;

public interface ITestEndpointsService
{
    Task<Result<Discard>> SignalRAsync(SignalRReq reqBody, CancellationToken cancellationToken = default);
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

    public async Task<Result<Discard>> SignalRAsync(SignalRReq reqBody, CancellationToken cancellationToken = default)
    {
        try
        {
            var res = await _httpService.PostAsync<SignalRReq, Discard>(
                Endpoints.SignalR, 
                reqBody,
                cancellationToken
            );
            return res.LogErrors(_logger, "SignalR call");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "An error occured.");
            return Result.Error();
        }
    }
}
