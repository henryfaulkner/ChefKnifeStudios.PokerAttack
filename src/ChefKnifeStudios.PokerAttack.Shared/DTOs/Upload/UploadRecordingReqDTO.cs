namespace ChefKnifeStudios.PokerAttack.Shared.DTOs.Upload;

public class UploadRecordingReqDTO
{
    public required string GameId { get; init; }
    public required string PlayerId { get; init; }
    public required byte[] RecordingData { get; init; }
    public string? Filename { get; init; }
}
