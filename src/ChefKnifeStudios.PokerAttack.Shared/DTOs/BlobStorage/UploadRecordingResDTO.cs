namespace ChefKnifeStudios.PokerAttack.Shared.DTOs.BlobStorage;

public class UploadRecordingResDTO
{
    public required string Url { get; init; }
    public required string GameId { get; init; }
    public required string PlayerId { get; init; }
    public required DateTime UploadedAt { get; init; }
}
