namespace ChefKnifeStudios.PokerAttack.Server.Data.Models;

public class Round : BaseEntity
{
    public ICollection<UserRound>? UserRounds { get; init; }
}
