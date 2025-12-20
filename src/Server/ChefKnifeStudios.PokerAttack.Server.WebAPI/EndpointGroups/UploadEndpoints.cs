using Ardalis.Result;
using ChefKnifeStudios.PokerAttack.Server.BL.Services;
using ChefKnifeStudios.PokerAttack.Shared.DTOs.Upload;

using Endpoints = ChefKnifeStudios.PokerAttack.Shared.PokerAttackApiEndpoints.Upload;

namespace ChefKnifeStudios.PokerAttack.Server.WebAPI.EndpointGroups;

public static class UploadEndpoints
{
    public static IEndpointRouteBuilder MapUploadEndpoints(this IEndpointRouteBuilder builder)
    {
        var group = builder
            .MapGroup(string.Empty)
            .WithName("Upload")
            .WithTags("Upload");

        group.MapPost(Endpoints.PublishRecording, async (
            UploadRecordingReqDTO reqBody,
            IStorageService storageService,
            CancellationToken cancellationToken = default) =>
        {
            // Validate request
            if (reqBody.RecordingData == null || reqBody.RecordingData.Length == 0)
            {
                return Result.Error("Recording data is required");
            }

            // Determine filename
            var filename = string.IsNullOrWhiteSpace(reqBody.Filename)
                ? $"recording_{DateTime.UtcNow:yyyyMMddHHmmss}.bin"
                : reqBody.Filename;

            // Get current date in MMDDYYYY format
            var dateFolder = DateTime.UtcNow.ToString("MMddyyyy");

            // Upload to storage
            // Directory structure: recordings/{MMDDYYYY}/
            var url = await storageService.StoreAsync(
                containerName: "game-recordings",
                directoryNames: new[] { "recordings", dateFolder },
                filename: filename,
                dataToStore: reqBody.RecordingData,
                cancellationToken: cancellationToken
            );

            var response = new UploadRecordingResDTO
            {
                Url = url,
                GameId = reqBody.GameId,
                PlayerId = reqBody.PlayerId,
                UploadedAt = DateTime.UtcNow
            };

            return Result.Success(response);
        })
        .WithName(nameof(Endpoints.PublishRecording))
        .Accepts<UploadRecordingReqDTO>("application/json")
        .Produces<UploadRecordingResDTO>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status500InternalServerError);

        return builder;
    }
}
