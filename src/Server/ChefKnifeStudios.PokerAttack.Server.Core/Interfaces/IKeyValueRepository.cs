namespace ChefKnifeStudios.PokerAttack.Server.Core.Interfaces;

public interface IKeyValueRepository<TValue>
{
    Task AddAsync(string key, TValue value, CancellationToken ct = default);
    Task<TValue?> GetAsync(string key, CancellationToken ct = default);
    Task UpdateAsync(string key, TValue value, CancellationToken ct = default);
    Task DeleteAsync(string key, CancellationToken ct = default);
    Task<IReadOnlyDictionary<string, TValue>> GetAllAsync(CancellationToken ct = default);
}
