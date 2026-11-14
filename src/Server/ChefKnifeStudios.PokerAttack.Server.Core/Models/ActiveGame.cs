using ChefKnifeStudios.PokerAttack.Shared.Enums;

namespace ChefKnifeStudios.PokerAttack.Server.Core.Models;

public class ActiveGame
{
    public required string Id { get; set; }
    public HashSet<Player> Players { get; set; } = new();
    public int RoundNumber { get; set; } = 0;

    /// <summary>Denormalized from the GameStates key-value entry so resync and reconciliation need one read, not two.</summary>
    public GameStates? CurrentPhase { get; set; }

    /// <summary>UTC. The persisted deadline replacing Task.Delay. Null when the phase is untimed.</summary>
    public DateTime? PhaseExpiresAt { get; set; }

    /// <summary>UTC. Lets a returning client compute elapsed as well as remaining.</summary>
    public DateTime? PhaseStartedAt { get; set; }
}