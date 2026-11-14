using Ardalis.Result;
using ChefKnifeStudios.PokerAttack.Server.Core.Interfaces;
using ChefKnifeStudios.PokerAttack.Server.Data;
using ChefKnifeStudios.PokerAttack.Server.Data.Models;
using ChefKnifeStudios.PokerAttack.Shared;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace ChefKnifeStudios.PokerAttack.Server.Infrastructure;

/// <summary>
/// Durable IKeyValueRepository&lt;T&gt; implementation over the state_kv table. One table
/// serves all value types, discriminated by "type" (the CLR type name). Serializes with
/// JsonOptions.Get() for converter and TypeInfoResolver parity with the wire format, since
/// values may hold polymorphic types (Deck/Card/ItemBase).
/// </summary>
public class PostgresKeyValueRepository<TValue>(AppDbContext dbContext) : IKeyValueRepository<TValue>
{
    static readonly string TypeName = typeof(TValue).Name;
    static readonly JsonSerializerOptions SerializerOptions = JsonOptions.Get();

    public async Task<Result> AddAsync(string key, TValue value, CancellationToken ct = default)
    {
        var existing = await dbContext.StateKeyValues.FindAsync([TypeName, key], ct);
        if (existing is not null)
            return Result.Conflict($"Key '{key}' already exists.");

        dbContext.StateKeyValues.Add(new StateKeyValue
        {
            Key = key,
            Type = TypeName,
            Value = JsonSerializer.Serialize(value, SerializerOptions),
            ExpiresAt = null,
            UpdatedAt = DateTime.UtcNow,
        });

        await dbContext.SaveChangesAsync(ct);
        return Result.Success();
    }

    public async Task<Result> AddAsync(string key, TValue value, DateTime expiresAt, CancellationToken ct = default)
    {
        var existing = await dbContext.StateKeyValues.FindAsync([TypeName, key], ct);
        if (existing is not null)
            return Result.Conflict($"Key '{key}' already exists.");

        dbContext.StateKeyValues.Add(new StateKeyValue
        {
            Key = key,
            Type = TypeName,
            Value = JsonSerializer.Serialize(value, SerializerOptions),
            ExpiresAt = expiresAt,
            UpdatedAt = DateTime.UtcNow,
        });

        await dbContext.SaveChangesAsync(ct);
        return Result.Success();
    }

    public async Task<Result<TValue?>> GetAsync(string key, CancellationToken ct = default)
    {
        var row = await dbContext.StateKeyValues
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Type == TypeName && x.Key == key, ct);

        if (row is null)
            return Result.Success<TValue?>(default);

        var value = JsonSerializer.Deserialize<TValue>(row.Value, SerializerOptions);
        return Result.Success(value);
    }

    public async Task<Result> UpdateAsync(string key, TValue value, CancellationToken ct = default)
    {
        var row = await dbContext.StateKeyValues.FindAsync([TypeName, key], ct);
        if (row is null)
            return Result.NotFound($"Key '{key}' not found.");

        row.Value = JsonSerializer.Serialize(value, SerializerOptions);
        row.UpdatedAt = DateTime.UtcNow;

        await dbContext.SaveChangesAsync(ct);
        return Result.Success();
    }

    public async Task<Result> DeleteAsync(string key, CancellationToken ct = default)
    {
        var row = await dbContext.StateKeyValues.FindAsync([TypeName, key], ct);
        if (row is not null)
        {
            dbContext.StateKeyValues.Remove(row);
            await dbContext.SaveChangesAsync(ct);
        }

        return Result.Success();
    }

    public async Task<Result<IReadOnlyDictionary<string, TValue>>> GetAllAsync(CancellationToken ct = default)
    {
        var rows = await dbContext.StateKeyValues
            .AsNoTracking()
            .Where(x => x.Type == TypeName)
            .ToListAsync(ct);

        var result = rows.ToDictionary(
            x => x.Key,
            x => JsonSerializer.Deserialize<TValue>(x.Value, SerializerOptions)!);

        return Result.Success<IReadOnlyDictionary<string, TValue>>(result);
    }
}
