using ChefKnifeStudios.PokerAttack.Client.Core.Services;
using ChefKnifeStudios.PokerAttack.Client.Shared.EventArgs.ModalEvents;
using Microsoft.AspNetCore.Components;

namespace ChefKnifeStudios.PokerAttack.Client.Shared.Components.Modals;

public partial class HowToPlayModal : ComponentBase
{
    [Inject] IBlobAccessService BlobAccessService { get; set; } = null!;
    [Inject] IEventNotificationService EventNotificationService { get; set; } = null!;

    void HandleClosePressed()
    {
        EventNotificationService.PostEvent(this, new HowToPlayModalEventArgs
        {
            ModalAction = ModalEventArgs.ModalActions.Close
        });
    }

    async Task GetSoloGameplayVideo(string fileExtension) => await BlobAccessService.GetBlobUrlWithSasAsync("video", $"SoloGameplayVideo.{fileExtension}");
}
