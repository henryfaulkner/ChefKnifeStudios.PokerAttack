using ChefKnifeStudios.PokerAttack.Client.Shared.ViewModels;
using Microsoft.AspNetCore.Components;

namespace ChefKnifeStudios.PokerAttack.Client.Shared.Components.Gameplay;

public partial class ScoreboardList : ComponentBase
{
    [Parameter] public required string GameId { get; set; } = null!;

    [Inject] IScoreboardViewModel ScoreboardViewModel { get; set; } = null!;
}
