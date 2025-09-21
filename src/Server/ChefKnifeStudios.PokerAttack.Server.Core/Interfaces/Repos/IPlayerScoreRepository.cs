namespace ChefKnifeStudios.PokerAttack.Server.Core.Interfaces.Repos;

public interface IPlayerScoreRepository
{
    Task AddAsync(string playerId, int score, CancellationToken cancellationToken = default);
    Task UpdateAsync(string playerId, int score, CancellationToken cancellationToken = default);
    Task<int?> GetAsync(string playerId, CancellationToken cancellationToken = default);
    Task DeleteAsync(string playerId, CancellationToken cancellationToken = default);
    Task<Dictionary<string, int>> GetAllAsync(CancellationToken cancellationToken = default);
}
