using ChefKnifeStudios.PokerAttack.Shared.DTOs.Gameplay;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ChefKnifeStudios.PokerAttack.Client.Shared.Models;

public partial class ScoreboardListItem : ObservableObject
{
    public ScoreboardListItem(RoundDTO.RoundScoreDTO roundScore)
    {
        ClientUserId = roundScore.ClientUserId;
        ClientUserDisplayName = roundScore.ClientUserDisplayName;
        Score = roundScore.Score;
        IsEliminating = false;
        IsEliminated = false;
    }

    [ObservableProperty]
    public string _clientUserId = string.Empty;
    [ObservableProperty]
    public string _clientUserDisplayName = string.Empty;
    [ObservableProperty]
    public int _score;
    [ObservableProperty]
    public bool _isEliminating;
    [ObservableProperty]
    public bool _isEliminated;
}
