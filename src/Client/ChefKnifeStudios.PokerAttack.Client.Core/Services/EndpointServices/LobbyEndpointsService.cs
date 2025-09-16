using ChefKnifeStudios.PokerAttack.Client.Core.Enums;
using Microsoft.Extensions.Logging;

namespace ChefKnifeStudios.PokerAttack.Client.Core.Services.EndpointServices;

public interface ILobbyEndpointsService
{
    
}

public class LobbyEndpointsService
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


}
