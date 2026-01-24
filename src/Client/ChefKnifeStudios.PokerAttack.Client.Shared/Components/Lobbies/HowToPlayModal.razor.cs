using ChefKnifeStudios.PokerAttack.Client.Core.Services;
using Microsoft.AspNetCore.Components;

namespace ChefKnifeStudios.PokerAttack.Client.Shared.Components.Lobbies;

public partial class HowToPlayModal : ComponentBase
{
    [Parameter] public string? GameResult { get; set; }
    [Parameter] public EventCallback OnClose { get; set; }

    [Inject] IBlobAccessService BlobAccessService { get; set; } = null!;

    async Task HandleClosePressed()
    {
        await OnClose.InvokeAsync();
    }

    async Task GetSoloGameplayVideo(string fileExtension) => await BlobAccessService.GetBlobUrlWithSasAsync("video", $"SoloGameplayVideo.{fileExtension}");
}
