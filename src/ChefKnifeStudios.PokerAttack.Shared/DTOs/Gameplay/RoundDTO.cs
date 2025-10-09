namespace ChefKnifeStudios.PokerAttack.Shared.DTOs.Gameplay;

public class RoundDTO
{
    public List<RoundScoreDTO> Scores { get; init; } = [];

    public class RoundScoreDTO
    {
        public string ClientUserId { get; init; } = string.Empty;
        public string ClientUserDisplayName { get; init; } = string.Empty;
        public int Score { get; init; }
    }
}

