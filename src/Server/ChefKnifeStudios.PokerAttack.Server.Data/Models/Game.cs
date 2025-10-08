namespace ChefKnifeStudios.PokerAttack.Server.Data.Models;

public class Game : BaseEntity
{
    public required string ClientId { get; init; }

    public ICollection<UserGame>? UserGames { get; init; }
}
