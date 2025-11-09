namespace ChefKnifeStudios.PokerAttack.Shared.DTOs.SignalR;

public record PokerAttackNotification(
    PokerAttackNotificationType NotificationType,
    string? Payload);

public enum PokerAttackNotificationType
{
    LobbyCreated,
    PlayerJoined,
    PlayerLeft,
    LobbyShutdown,
    PlayerUpdated,
    GameStarted,
    LobbiesChanged,

    Test,

    RunStarted,
    CardsDealt,
    HandPlayed,
    RoundEnded,
    EliminationStarted,
    EliminationFinished,
    GameLost,
    GameWon,

    GameStateChanged,

    PlayerPowersReadied,

    MessageSent,
}