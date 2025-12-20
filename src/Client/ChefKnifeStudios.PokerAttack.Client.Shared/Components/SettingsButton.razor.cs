using ChefKnifeStudios.PokerAttack.Client.Core.Services;
using ChefKnifeStudios.PokerAttack.Client.Shared.EventArgs;
using Microsoft.AspNetCore.Components;

namespace ChefKnifeStudios.PokerAttack.Client.Shared.Components;

public partial class SettingsButton : ComponentBase
{
    [Inject] IEventNotificationService NotificationService { get; set; } = null!;

    async Task HandleCogPressed()
    {
        NotificationService.PostEvent(
            this,
            new BladeEventArgs() { Type = BladeEventArgs.Types.Settings, }
        );
    }
}
