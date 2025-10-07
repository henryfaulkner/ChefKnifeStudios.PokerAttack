using ChefKnifeStudios.PokerAttack.Client.Shared.Models;
using ChefKnifeStudios.PokerAttack.Client.Shared.Services;
using Microsoft.AspNetCore.Components;

namespace ChefKnifeStudios.PokerAttack.Client.Shared.Components.Gameplay;

public partial class CardHand : ComponentBase
{
    [Parameter] public List<CardItem> Cards { get; set; } = [];

    [Inject] ICardImageService CardImageService { get; set; } = null!;
}
