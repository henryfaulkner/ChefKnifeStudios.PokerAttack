using ChefKnifeStudios.PokerAttack.Client.Shared.Constants;
using ChefKnifeStudios.PokerAttack.Client.Shared.Models;
using ChefKnifeStudios.PokerAttack.Client.Shared.Services;
using Microsoft.AspNetCore.Components;

namespace ChefKnifeStudios.PokerAttack.Client.Shared.Components.Gameplay;

public partial class CardHand : ComponentBase
{
    [Parameter] public List<CardItem> Cards { get; set; } = [];

    [Inject] IAudioService AudioService { get; set; } = null!;
    [Inject] ICardImageService CardImageService { get; set; } = null!;

    void HandleCardPressed(CardItem card)
    {
        card.IsSelected = !card.IsSelected;
        _ = AudioService.PlayOneShotAsync(FilePaths.PlaceCardThree);
    }
}
