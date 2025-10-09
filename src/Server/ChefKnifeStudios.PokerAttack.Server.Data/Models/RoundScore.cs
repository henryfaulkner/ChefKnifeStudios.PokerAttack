namespace ChefKnifeStudios.PokerAttack.Server.Data.Models;

public class RoundScore : BaseEntity
{
    public int RoundId { get; init; }
    public required string ClientUserId { get; init; }
    public required string ClientUserDisplayName { get; init; }
    public int Score { get; init; }

    public Round? Round { get; init; }
}
