namespace ChefKnifeStudios.PokerAttack.Server.Data.Models;

public class Round : BaseEntity
{
    public int GameId { get; init; }

    public Game? Game { get; init; }
    public ICollection<RoundScore>? RoundScores { get; init; }
}
