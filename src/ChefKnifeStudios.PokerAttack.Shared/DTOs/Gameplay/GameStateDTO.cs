using ChefKnifeStudios.PokerAttack.Shared.Enums;

namespace ChefKnifeStudios.PokerAttack.Shared.DTOs.Gameplay;

/// <summary>
/// Rehydrates a returning client after an interruption: group rejoin only restores future
/// broadcasts, this fills in everything that happened while the client was away.
/// </summary>
public class GameStateDTO
{
    public required string GameId { get; init; }
    public int RoundNumber { get; init; }
    public GameStates? CurrentPhase { get; init; }
    public DateTime? PhaseStartedAt { get; init; }
    public DateTime? PhaseExpiresAt { get; init; }

    /// <summary>Client derives remaining time as PhaseExpiresAt - ServerTimeUtc rather than trusting its own clock.</summary>
    public required DateTime ServerTimeUtc { get; init; }

    public required ResyncPlayerDTO Player { get; init; }
    public required IReadOnlyList<ResyncOpponentDTO> Opponents { get; init; }
}

public class ResyncPlayerDTO
{
    public required string PlayerId { get; init; }
    public required IEnumerable<CardDTO> CardsInHand { get; init; }
    public int Score { get; init; }
    public int Wallet { get; init; }
    public int HandsRemaining { get; init; }
    public int DiscardsRemaining { get; init; }
    public bool IsEliminating { get; init; }
    public bool IsEliminated { get; init; }
    public PlayerPowerDTO? PlayerPower { get; init; }
    public required IEnumerable<ItemBase> PurchasedItems { get; init; }
}

/// <summary>
/// Opponents expose score and elimination status only — never hand, wallet, deck, or items.
/// Resync must not become a side channel that reveals what live play conceals.
/// </summary>
public class ResyncOpponentDTO
{
    public required string PlayerId { get; init; }
    public required string Name { get; init; }
    public int Score { get; init; }
    public bool IsEliminating { get; init; }
    public bool IsEliminated { get; init; }
}
