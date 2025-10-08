namespace ChefKnifeStudios.PokerAttack.Server.Data.Models;

public class UserRound : BaseEntity
{
    public int UserId { get; init; }
    public int RoundId { get; init; }
    public int Score { get; init; }

    public User? User { get; init; }
    public Round? Round { get; init; }
}
