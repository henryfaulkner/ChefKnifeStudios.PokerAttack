using Ardalis.Specification;
using ChefKnifeStudios.PokerAttack.Server.Data.Models;

namespace ChefKnifeStudios.PokerAttack.Server.Data.Specifications;

public sealed class GetLatestRoundByGameIdSpec : Specification<Round>
{
    public GetLatestRoundByGameIdSpec(int gameId)
    {
        Query
            .Include(x => x.Game)
            .Include(x => x.RoundScores)
            .Where(x => x.GameId == gameId)
            .OrderByDescending(x => x.CreatedOnUtc);
    }
}
