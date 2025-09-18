using ChefKnifeStudios.PokerAttack.Client.Shared.ViewModels;
using Microsoft.AspNetCore.Components;

namespace ChefKnifeStudios.PokerAttack.Client.Shared.Components;

public partial class LobbyList : ComponentBase
{
    [Inject] IApplicationViewModel ApplicationViewModel { get; set; } = null!;
    [Inject] ILobbyViewModel LobbyViewModel { get; set; } = null!;

    protected override async Task OnInitializedAsync()
    {
        await base.OnInitializedAsync();
        await LobbyViewModel.LoadLobbiesAsync();
    }

    void HandleJoinLobbyPressed(string gameId)
    {
        _ = LobbyViewModel.JoinLobbyAsync(gameId, ApplicationViewModel.PlayerId);
    }
}
