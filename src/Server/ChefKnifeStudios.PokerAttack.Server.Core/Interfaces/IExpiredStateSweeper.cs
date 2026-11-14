namespace ChefKnifeStudios.PokerAttack.Server.Core.Interfaces;

/// <summary>
/// Deletes state_kv rows whose expires_at has passed, across all value types in one query.
/// IKeyValueRepository&lt;T&gt; is scoped per type and never surfaces expires_at, so a
/// cross-type TTL sweep needs this separate seam.
/// </summary>
public interface IExpiredStateSweeper
{
    Task<IReadOnlyList<(string Type, string Key)>> SweepExpiredAsync(DateTime now, CancellationToken ct = default);
}
