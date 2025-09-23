using AutoFixture;
using ChefKnifeStudios.PokerAttack.Client.Shared.Services;
using ChefKnifeStudios.PokerAttack.Shared.Enums;
using Microsoft.AspNetCore.Components;

namespace ChefKnifeStudios.PokerAttack.Client.Shared.Components;

public partial class CardHand : ComponentBase
{
    [Inject] ICardImageService CardImageService { get; set; } = null!;

    List<CardItem> _cards = new();

    protected override void OnInitialized()
    {
        base.OnInitialized();

        var fixture = new Fixture();
        _cards = fixture.CreateMany<CardItem>(5).ToList();
        foreach (var card in _cards) card.IsSelected = false;
    }

    class CardItem
    {
        public Suits Suit { get; init; }
        public Ranks Rank { get; init; }
        public bool IsSelected { get; set; }
    }
}
