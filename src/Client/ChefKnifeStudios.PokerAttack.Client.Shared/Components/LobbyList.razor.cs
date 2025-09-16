using ChefKnifeStudios.PokerAttack.Client.Shared.ViewModels;
using Microsoft.AspNetCore.Components;

namespace ChefKnifeStudios.PokerAttack.Client.Shared.Components;

public partial class LobbyList : ComponentBase
{
    [Inject] ILobbyViewModel LobbyViewModel { get; set; } = null!;

    protected override void OnInitialized()
    {
        _ = LobbyViewModel.LoadLobbiesAsync();
    }

    void HandleJoinLobbyPressed()
    {
        throw new NotImplementedException();
    }
}
