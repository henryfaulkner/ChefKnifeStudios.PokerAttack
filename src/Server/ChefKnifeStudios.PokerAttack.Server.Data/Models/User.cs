namespace ChefKnifeStudios.PokerAttack.Server.Data.Models;

public class User : BaseEntity
{
    public required string ClientId { get; init; }
    public required string DisplayName { get; init; }

    public ICollection<UserGame>? UserGames { get; init; }
    public ICollection<UserRound>? UserRounds { get; init; }
}
