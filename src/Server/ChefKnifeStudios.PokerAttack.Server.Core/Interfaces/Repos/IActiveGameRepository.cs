using ChefKnifeStudios.PokerAttack.Server.Core.Models;

namespace ChefKnifeStudios.PokerAttack.Server.Core.Interfaces.Repos;

public interface IActiveGameRepository
{
    Task AddAsync(string gameId, ActiveGame game, CancellationToken ct = default);
    Task<ActiveGame?> GetAsync(string gameId, CancellationToken ct = default);
    Task UpdateAsync(string gameId, ActiveGame game, CancellationToken ct = default);
    Task DeleteAsync(string gameId, CancellationToken ct = default);
    Task<IReadOnlyDictionary<string, ActiveGame>> GetAllAsync(CancellationToken ct = default);
}
