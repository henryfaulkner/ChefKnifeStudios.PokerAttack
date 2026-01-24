using Ardalis.Result;
using ChefKnifeStudios.PokerAttack.Server.BL.Services;
using ChefKnifeStudios.PokerAttack.Shared.DTOs.BlobStorage;

using Endpoints = ChefKnifeStudios.PokerAttack.Shared.PokerAttackApiEndpoints.BlobStorage;

namespace ChefKnifeStudios.PokerAttack.Server.WebAPI.EndpointGroups;

public static class BlobStorageEndpoints
{
    public static IEndpointRouteBuilder MapBlobStorageEndpoints(this IEndpointRouteBuilder builder)
    {
        var group = builder
            .MapGroup(string.Empty)
            .WithName("BlobStorage")
            .WithTags("BlobStorage");

        group.MapPost(Endpoints.UploadRecording, async (
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
        .WithName(nameof(Endpoints.UploadRecording))
        .Accepts<UploadRecordingReqDTO>("application/json")
        .Produces<UploadRecordingResDTO>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status500InternalServerError);

        group.MapGet(Endpoints.GenerateSasToken, async (
            string containerName,
            string blobPath,
            int? duration,
            IStorageService storageService,
            CancellationToken cancellationToken = default) =>
        {
            // Validate request
            if (string.IsNullOrWhiteSpace(containerName))
            {
                return Results.BadRequest("Container name is required");
            }

            if (string.IsNullOrWhiteSpace(blobPath))
            {
                return Results.BadRequest("Blob path is required");
            }

            var durationInMinutes = duration ?? 5;

            // Generate SAS token using cached User Delegation Key
            var sasUrl = await storageService.GenerateUserDelegationSasAsync(
                containerName,
                blobPath,
                durationInMinutes,
                cancellationToken
            );

            var expiresOn = DateTimeOffset.UtcNow.AddMinutes(durationInMinutes);

            var response = new GenerateSasTokenResDTO(sasUrl, expiresOn);

            return Results.Ok(Result.Success(response));
        })
        .WithName(nameof(Endpoints.GenerateSasToken))
        .Produces<GenerateSasTokenResDTO>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status500InternalServerError);

        return builder;
    }
}
