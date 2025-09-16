using Ardalis.Result;
using ChefKnifeStudios.PokerAttack.Client.Core.Enums;
using ChefKnifeStudios.PokerAttack.Client.Core.Extensions;
using ChefKnifeStudios.PokerAttack.Shared.DTOs.Lobby;
using Microsoft.Extensions.Logging;

using Endpoints = ChefKnifeStudios.PokerAttack.Shared.PokerAttackApiEndpoints.Lobby;

namespace ChefKnifeStudios.PokerAttack.Client.Core.Services.EndpointServices;

public interface ILobbyEndpointsService
{
    Task<Result<IEnumerable<LobbyDTO>?>> GetLobbiesAsync(CancellationToken cancellationToken = default);
    Task<Result<LobbyDTO?>> GetLobbyAsync(string gameId, CancellationToken cancellationToken = default);
    Task<Result<Discard>> AddPlayerAsync(AddPlayerReqDTO reqBody, CancellationToken cancellationToken = default);
    Task<Result<Discard>> RemovePlayerAsync(RemovePlayerReqDTO reqBody, CancellationToken cancellationToken = default);
    Task<Result<Discard>> ShutdownLobbyAsync(string gameId, CancellationToken cancellationToken = default);
}

public class LobbyEndpointsService : ILobbyEndpointsService
{
    readonly ILogger<LobbyEndpointsService> _logger;
    readonly IHttpService _httpService;

    public LobbyEndpointsService(
        ILogger<LobbyEndpointsService> logger,
        IHttpServiceFactory httpServiceFactory)
    {
        _logger = logger;
        _httpService = httpServiceFactory.Create(nameof(APIs.PokerAttackAPI));
    }

    public async Task<Result<IEnumerable<LobbyDTO>?>> GetLobbiesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var res = await _httpService.GetAsync<IEnumerable<LobbyDTO>?>(
                Endpoints.GetLobbies,
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

    public async Task<Result<LobbyDTO?>> GetLobbyAsync(string gameId, CancellationToken cancellationToken = default)
    {
        try
        {
            var res = await _httpService.GetAsync<LobbyDTO?> (
                Endpoints.GetLobby.FormatRoute(gameId),
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

    public async Task<Result<Discard>> AddPlayerAsync(AddPlayerReqDTO reqBody, CancellationToken cancellationToken = default)
    {
        try
        {
            var res = await _httpService.PatchAsync<AddPlayerReqDTO, Discard> (
                Endpoints.AddPlayer,
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

    public async Task<Result<Discard>> RemovePlayerAsync(RemovePlayerReqDTO reqBody, CancellationToken cancellationToken = default)
    {
        try
        {
            var res = await _httpService.PatchAsync<RemovePlayerReqDTO, Discard> (
                Endpoints.RemovePlayer,
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

    public async Task<Result<Discard>> ShutdownLobbyAsync(string gameId, CancellationToken cancellationToken = default)
    {
        try
        {
            var res = await _httpService.DeleteAsync<Discard> (
                Endpoints.ShutdownLobby.FormatRoute(gameId),
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
