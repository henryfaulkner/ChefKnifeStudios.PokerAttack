namespace ChefKnifeStudios.PokerAttack.Shared.DTOs;

public class MessageDTO
{
    public string? Title { get; init; }
    public required string Message { get; init; }
    public MessageType Type { get; init; }

    public enum MessageType
    {
        Success,
        Warning, 
        Error,
    }
}
