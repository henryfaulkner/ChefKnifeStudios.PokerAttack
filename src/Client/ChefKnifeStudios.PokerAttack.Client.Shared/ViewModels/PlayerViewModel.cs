using ChefKnifeStudios.PokerAttack.Client.Core.Services.EndpointServices;
using ChefKnifeStudios.PokerAttack.Client.Shared.Services;
using ChefKnifeStudios.PokerAttack.Shared.DTOs.Lobby;

namespace ChefKnifeStudios.PokerAttack.Client.Shared.ViewModels;

public interface IPlayerViewModel : IViewModel
{
    Task UpdatePlayerNameAsync(PlayerDTO player, string newName, CancellationToken cancellationToken = default);
}

public class PlayerViewModel(
    ILobbyEndpointsService lobbyEndpointsService,
    IToastService toastService) : BaseViewModel, IPlayerViewModel
{
    public async Task UpdatePlayerNameAsync(PlayerDTO player, string newName, CancellationToken cancellationToken = default)
    {
        player.Name = newName;

        var res = await lobbyEndpointsService.UpdatePlayerAsync(player, cancellationToken);

        if (!res.IsSuccess)
        { 
            toastService.ShowError("Name failed to update");
            return;
        }

        toastService.ShowSuccess("Name updated");
    }
}
