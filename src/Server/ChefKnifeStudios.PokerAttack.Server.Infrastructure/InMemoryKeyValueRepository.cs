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

    public virtual Task AddAsync(string key, TValue value, CancellationToken ct = default)
    {
        if (!_store.TryAdd(key, value))
            throw new InvalidOperationException($"Key '{key}' already exists.");

        return Task.CompletedTask;
    }

    public virtual Task<TValue?> GetAsync(string key, CancellationToken ct = default)
    {
        _store.TryGetValue(key, out var value);
        return Task.FromResult(value);
    }

    public virtual Task UpdateAsync(string key, TValue value, CancellationToken ct = default)
    {
        if (!_store.ContainsKey(key))
            throw new KeyNotFoundException($"Key '{key}' not found.");

        _store[key] = value;
        return Task.CompletedTask;
    }

    public virtual Task DeleteAsync(string key, CancellationToken ct = default)
    {
        _store.TryRemove(key, out _);
        return Task.CompletedTask;
    }

    public virtual Task<IReadOnlyDictionary<string, TValue>> GetAllAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyDictionary<string, TValue>>(_store);
}