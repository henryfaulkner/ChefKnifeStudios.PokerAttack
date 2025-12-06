using Blazored.LocalStorage;
using ChefKnifeStudios.PokerAttack.Client.Core.Services.EndpointServices;
using ChefKnifeStudios.PokerAttack.Client.Shared.Constants;
using ChefKnifeStudios.PokerAttack.Shared.DTOs.Lobby;
using NameGenerator.Generators;

namespace ChefKnifeStudios.PokerAttack.Client.Shared.ViewModels;

public interface IPlayerViewModel : IViewModel
{
    Task UpdatePlayerNameAsync(PlayerDTO player, string newName, CancellationToken cancellationToken = default);
    Task<string> RandomizePlayerNameAsync(PlayerDTO player, CancellationToken cancellationToken = default);
}

public partial class PlayerViewModel : BaseViewModel, IPlayerViewModel
{
    readonly IApplicationViewModel _applicationViewModel;
    readonly ILobbyEndpointsService _lobbyEndpointsService;
    readonly ILocalStorageService _localStorageService;

    public PlayerViewModel(
        IApplicationViewModel applicationViewModel,
        ILobbyEndpointsService lobbyEndpointsService,
        ILocalStorageService localStorageService)
    {
        _applicationViewModel = applicationViewModel;
        _lobbyEndpointsService = lobbyEndpointsService;
        _localStorageService = localStorageService;
    }

    public async Task UpdatePlayerNameAsync(PlayerDTO player, string newName, CancellationToken cancellationToken = default)
    {
        player.Name = newName;
        await _localStorageService.SetItemAsync(LocalStorageConstants.PlayerNameKey, newName, cancellationToken);
        _ = await _lobbyEndpointsService.UpdatePlayerAsync(player, cancellationToken);
    }

    public async Task<string> RandomizePlayerNameAsync(PlayerDTO player, CancellationToken cancellationToken = default)
    {
        var generator = new GamerTagGenerator();
        string result = generator.Generate();

        player.Name = result;
        await _localStorageService.SetItemAsync(LocalStorageConstants.PlayerNameKey, result, cancellationToken);
        _ = await _lobbyEndpointsService.UpdatePlayerAsync(player, cancellationToken);
        return result;
    }
}
