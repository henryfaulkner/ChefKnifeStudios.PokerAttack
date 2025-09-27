using ChefKnifeStudios.PokerAttack.Client.Shared.ViewModels;
using ChefKnifeStudios.PokerAttack.Shared.DTOs.Lobby;
using MatBlazor;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;

namespace ChefKnifeStudios.PokerAttack.Client.Shared.Components;

public partial class EditablePlayerName : ComponentBase
{
    [Parameter] public required PlayerDTO Player { get; set; }

    [Inject] IPlayerViewModel PlayerViewModel { get; set; } = null!;
    [Inject] ILogger<EditablePlayerName> Logger { get; set; } = null!;

    bool _isEditing = false;
    string _name = string.Empty;
    MatTextField<string>? _textField = null;

    protected override void OnInitialized()
    {
        base.OnInitialized();
        _name = Player.Name;
    }

    async Task HandleEditingStarted()
    {
        _isEditing = true;
        await _textField!.Ref.FocusAsync();
    }

    async Task HandleEditingStopped()
    {
        if (_name == Player.Name)
        {
            _isEditing = false;
            return;
        }

        try
        {
            await PlayerViewModel.UpdatePlayerNameAsync(Player, _name);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "An error has occurred.");
        }
        finally
        {
            _isEditing = false;
        }
    }
}
