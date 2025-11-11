using ChefKnifeStudios.PokerAttack.Client.Core.Services.EndpointServices;
using ChefKnifeStudios.PokerAttack.Client.Shared.Services;
using ChefKnifeStudios.PokerAttack.Shared.DTOs.Lobby;

namespace ChefKnifeStudios.PokerAttack.Client.Shared.ViewModels;

public interface IPlayerViewModel : IViewModel
{
    Task UpdatePlayerNameAsync(PlayerDTO player, string newName, CancellationToken cancellationToken = default);
}

public class PlayerViewModel(ILobbyEndpointsService lobbyEndpointsService) : BaseViewModel, IPlayerViewModel
{
    public async Task UpdatePlayerNameAsync(PlayerDTO player, string newName, CancellationToken cancellationToken = default)
    {
        player.Name = newName;
        _ = await lobbyEndpointsService.UpdatePlayerAsync(player, cancellationToken);
    }
}
