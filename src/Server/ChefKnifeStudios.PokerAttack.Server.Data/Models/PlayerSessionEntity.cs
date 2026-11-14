namespace ChefKnifeStudios.PokerAttack.Server.Data.Models;

/// <summary>
/// Backing row for the player_session table. Connection ids are deliberately not stored —
/// they are meaningless across a restart. Source of truth for grace-period membership,
/// reached through IPlayerSessionStore rather than IKeyValueRepository&lt;T&gt; because its
/// queries are relational (find by game_id, find overdue by disconnected_at).
/// </summary>
public class PlayerSessionEntity
{
    public required string PlayerId { get; set; }
    public string? GameId { get; set; }
    public string? LobbyId { get; set; }
    public bool Connected { get; set; }
    public DateTime? DisconnectedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
