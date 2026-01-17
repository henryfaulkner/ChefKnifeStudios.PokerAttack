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
    WagerCompleted,
    RoundEnded,
    EliminationStarted,
    EliminationFinished,
    GameLost,
    GameWon,

    GameStateChanged,

    PlayerPowersReadied,

    MessageSent,
}