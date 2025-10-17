using ChefKnifeStudios.PokerAttack.Server.Core.Models;

namespace ChefKnifeStudios.PokerAttack.Server.Core.Interfaces.Repos;

public interface IGamePlayerRepository
{
    Task AddAsync(string playerId, GamePlayer player, CancellationToken ct = default);
    Task<GamePlayer?> GetAsync(string playerId, CancellationToken ct = default);
    Task UpdateAsync(string playerId, GamePlayer player, CancellationToken ct = default);
    Task DeleteAsync(string playerId, CancellationToken ct = default);
    Task<IReadOnlyDictionary<string, GamePlayer>> GetAllAsync(CancellationToken ct = default);
}
