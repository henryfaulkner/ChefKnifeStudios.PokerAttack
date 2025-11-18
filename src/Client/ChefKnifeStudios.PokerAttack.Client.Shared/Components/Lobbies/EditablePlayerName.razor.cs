using ChefKnifeStudios.PokerAttack.Client.Shared.ViewModels;
using ChefKnifeStudios.PokerAttack.Shared.DTOs.Lobby;
using MatBlazor;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;

namespace ChefKnifeStudios.PokerAttack.Client.Shared.Components.Lobbies;

public partial class EditablePlayerName : ComponentBase
{
    [Parameter] public required PlayerDTO Player { get; set; }

    [Inject] IPlayerViewModel PlayerViewModel { get; set; } = null!;
    [Inject] ILogger<EditablePlayerName> Logger { get; set; } = null!;

    string _name = string.Empty;

    protected override void OnInitialized()
    {
        base.OnInitialized();
        _name = Player.Name;
    }

    async Task HandleEditingStarted() {}

    async Task HandleEditingStopped()
    {
        if (_name == Player.Name) return;

        try
        {
            await PlayerViewModel.UpdatePlayerNameAsync(Player, _name);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "An error has occurred.");
        }
    }
}
