using Ardalis.Specification;
using ChefKnifeStudios.PokerAttack.Server.Data.Models;

namespace ChefKnifeStudios.PokerAttack.Server.Data.Specifications;

public sealed class GetGameByClientIdSpec : Specification<Game>
{
    public GetGameByClientIdSpec(string clientId)
    {
        Query
            .Include(x => x.Rounds)
            .Where(x => x.ClientId == clientId);
    }
}
