using Ardalis.Result;
using ChefKnifeStudios.PokerAttack.Server.Core.Interfaces;
using System.Collections.Concurrent;

namespace ChefKnifeStudios.PokerAttack.Server.Infrastructure;

public class InMemoryKeyValueRepository<TValue> : IKeyValueRepository<TValue>
{
    private readonly ConcurrentDictionary<string, TValue> _store;

    public InMemoryKeyValueRepository(IEqualityComparer<string>? comparer = null)
    {
        _store = new ConcurrentDictionary<string, TValue>(comparer ?? StringComparer.OrdinalIgnoreCase);
    }

    public virtual Task<Result> AddAsync(string key, TValue value, CancellationToken ct = default)
    {
        if (!_store.TryAdd(key, value))
            return Task.FromResult(Result.Conflict($"Key '{key}' already exists."));

        return Task.FromResult(Result.Success());
    }

    public virtual Task<Result<TValue?>> GetAsync(string key, CancellationToken ct = default)
    {
        _store.TryGetValue(key, out var value);
        return Task.FromResult(Result.Success(value));
    }

    public virtual Task<Result> UpdateAsync(string key, TValue value, CancellationToken ct = default)
    {
        if (!_store.ContainsKey(key))
            return Task.FromResult(Result.NotFound($"Key '{key}' not found."));

        _store[key] = value;
        return Task.FromResult(Result.Success());
    }

    public virtual Task<Result> DeleteAsync(string key, CancellationToken ct = default)
    {
        _store.TryRemove(key, out _);
        return Task.FromResult(Result.Success());
    }

    public virtual Task<Result<IReadOnlyDictionary<string, TValue>>> GetAllAsync(CancellationToken ct = default)
        => Task.FromResult(Result.Success<IReadOnlyDictionary<string, TValue>>(_store));
}