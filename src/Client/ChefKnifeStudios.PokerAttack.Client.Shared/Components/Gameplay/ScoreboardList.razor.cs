using ChefKnifeStudios.PokerAttack.Client.Shared.ViewModels;
using Microsoft.AspNetCore.Components;

namespace ChefKnifeStudios.PokerAttack.Client.Shared.Components.Gameplay;

public partial class ScoreboardList : ComponentBase
{
    [Inject] public ScoreboardViewModel ScoreboardViewModel { get; set; } = null!;
}
