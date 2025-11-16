using ChefKnifeStudios.PokerAttack.Client.Shared.Constants;
using ChefKnifeStudios.PokerAttack.Client.Shared.Models;
using ChefKnifeStudios.PokerAttack.Client.Shared.Services;
using ChefKnifeStudios.PokerAttack.Client.Shared.Services.JsInterop;
using Microsoft.AspNetCore.Components;

namespace ChefKnifeStudios.PokerAttack.Client.Shared.Components.Gameplay;

public partial class CardHand : ComponentBase
{
    [Parameter] public List<CardItem> Cards { get; set; } = [];

    [Inject] IAudioJsInterop AudioJsInterop { get; set; } = null!;
    [Inject] ICardImageService CardImageService { get; set; } = null!;

    void HandleCardPressed(CardItem card)
    {
        card.IsSelected = !card.IsSelected;
        _ = AudioJsInterop.PlayOneShotAsync(FilePaths.PlaceCardThree);
    }
}
