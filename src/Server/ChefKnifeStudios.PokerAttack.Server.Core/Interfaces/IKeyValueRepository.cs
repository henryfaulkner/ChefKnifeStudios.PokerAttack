using Ardalis.Result;

namespace ChefKnifeStudios.PokerAttack.Server.Core.Interfaces;

public interface IKeyValueRepository<TValue>
{
    Task<Result> AddAsync(string key, TValue value, CancellationToken ct = default);

    /// <summary>Adds with a TTL. The value is eligible for cleanup once expiresAt has passed (FR-014).</summary>
    Task<Result> AddAsync(string key, TValue value, DateTime expiresAt, CancellationToken ct = default);
    Task<Result<TValue?>> GetAsync(string key, CancellationToken ct = default);
    Task<Result> UpdateAsync(string key, TValue value, CancellationToken ct = default);
    Task<Result> DeleteAsync(string key, CancellationToken ct = default);
    Task<Result<IReadOnlyDictionary<string, TValue>>> GetAllAsync(CancellationToken ct = default);
}
