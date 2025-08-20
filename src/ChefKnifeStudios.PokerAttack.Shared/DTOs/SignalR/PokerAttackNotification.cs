namespace ChefKnifeStudios.PokerAttack.Shared.DTOs.SignalR;

public record PokerAttackNotification(
    PokerAttackNotificationType NotificationType,
    object? Payload = null);

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