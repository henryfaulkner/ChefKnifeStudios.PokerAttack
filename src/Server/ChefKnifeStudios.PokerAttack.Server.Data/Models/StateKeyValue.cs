namespace ChefKnifeStudios.PokerAttack.Server.Data.Models;

/// <summary>
/// Backing row for PostgresKeyValueRepository&lt;T&gt;. Deliberately does not derive from
/// BaseEntity — this is a key-value store with a natural composite key (type, key) and no
/// soft-delete semantics, reached through IKeyValueRepository&lt;T&gt; rather than IRepository&lt;T&gt;.
/// </summary>
public class StateKeyValue
{
    public required string Key { get; set; }
    public required string Type { get; set; }
    public required string Value { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
