using ChefKnifeStudios.PokerAttack.Client.Core.Services.EndpointServices;
using ChefKnifeStudios.PokerAttack.Shared.DTOs.Lobby;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ChefKnifeStudios.PokerAttack.Client.Shared.ViewModels;

public interface ILobbyViewModel : IViewModel
{
    IEnumerable<LobbyDTO> Lobbies { get; }
    bool IsLoadingLobbies { get; }
    Task LoadLobbiesAsync();
}

public partial class LobbyViewModel : BaseViewModel, ILobbyViewModel
{
    readonly ILobbyEndpointsService _lobbyEndpointsService;

    public LobbyViewModel(
        ILobbyEndpointsService lobbyEndpointsService)
    {
        _lobbyEndpointsService = lobbyEndpointsService;
    }

    [ObservableProperty]
    IEnumerable<LobbyDTO> _lobbies = [];

    [ObservableProperty]
    bool _isLoadingLobbies = false;

    public async Task LoadLobbiesAsync()
    {
        IsLoadingLobbies = true;

        var res = await _lobbyEndpointsService.GetLobbiesAsync();
        if (res.IsSuccess && res.Value is IEnumerable<LobbyDTO>) 
            Lobbies = res.Value;

        IsLoadingLobbies = false;
    }
}
