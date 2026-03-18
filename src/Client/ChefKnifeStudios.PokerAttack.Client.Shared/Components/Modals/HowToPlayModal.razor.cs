using ChefKnifeStudios.PokerAttack.Client.Core.Services;
using ChefKnifeStudios.PokerAttack.Client.Shared.EventArgs.ModalEvents;
using Microsoft.AspNetCore.Components;

namespace ChefKnifeStudios.PokerAttack.Client.Shared.Components.Modals;

public partial class HowToPlayModal : ComponentBase
{
    [Inject] IEventNotificationService EventNotificationService { get; set; } = null!;

    void HandleOpenChanged(bool isOpen)
    {
        if (!isOpen)
        {
            EventNotificationService.PostEvent(this, new HowToPlayModalEventArgs
            {
                ModalAction = ModalEventArgs.ModalActions.Close
            });
        }
    }
}
