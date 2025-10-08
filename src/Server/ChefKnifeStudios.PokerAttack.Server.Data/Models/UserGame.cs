namespace ChefKnifeStudios.PokerAttack.Server.Data.Models;

public class UserGame : BaseEntity
{
    public int UserId { get; init; }
    public int GameId { get; init; }

    public User? User { get; init; }
    public Game? Game { get; init; }
}
