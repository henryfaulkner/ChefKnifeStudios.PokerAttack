using ChefKnifeStudios.PokerAttack.Shared.DTOs.Gameplay;

namespace ChefKnifeStudios.PokerAttack.Client.Shared.ViewModels;

public interface IScoreboardViewModel : IViewModel
{
    List<PlayerScoreDTO> PlayerScores { get; }
}

public partial class ScoreboardViewModel : BaseViewModel
{
}
