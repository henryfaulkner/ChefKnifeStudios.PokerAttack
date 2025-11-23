using Ardalis.Specification;
using ChefKnifeStudios.PokerAttack.Server.Data.Models;

namespace ChefKnifeStudios.PokerAttack.Server.Data.Specifications;

public sealed class GetRoundsByGameIdSpec : Specification<Round>
{
    public GetRoundsByGameIdSpec(int gameId)
    {
        Query
            .Where(x => x.Game is Game
                && x.Game.Id == gameId);
    }
}
