namespace ChefKnifeStudios.PokerAttack.Shared.DTOs.SignalR;

public record PokerAttackNotification(
    PokerAttackNotificationType NotificationType,
    string? Payload);

public enum PokerAttackNotificationType
{
    PlayerJoined,
    PlayerLeft,
    HandPlayed,
    PowerUsed,
    RoundStarted,
    RoundEnded,
    PlayerEliminated,
    WinnerDeclared
}