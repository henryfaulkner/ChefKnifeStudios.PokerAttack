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

    Test,

    RunStarted,
    CardsDealt,
    HandPlayed,
}