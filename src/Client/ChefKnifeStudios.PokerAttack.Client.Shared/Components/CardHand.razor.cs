using Microsoft.AspNetCore.Components;

namespace ChefKnifeStudios.PokerAttack.Client.Shared.Components;

public partial class CardHand : ComponentBase
{
    List<CardItem> _cards = new();

    protected override void OnInitialized()
    {
        base.OnInitialized();
        for (int i = 0; i < 5; i += 1) _cards.Add(new CardItem());
    }

    class CardItem
    {
        public bool IsSelected { get; set; }
    }
}
