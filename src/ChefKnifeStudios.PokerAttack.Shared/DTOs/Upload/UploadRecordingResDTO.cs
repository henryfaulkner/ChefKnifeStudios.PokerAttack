namespace ChefKnifeStudios.PokerAttack.Shared.DTOs.Upload;

public class UploadRecordingResDTO
{
    public required string Url { get; init; }
    public required string GameId { get; init; }
    public required string PlayerId { get; init; }
    public required DateTime UploadedAt { get; init; }
}
