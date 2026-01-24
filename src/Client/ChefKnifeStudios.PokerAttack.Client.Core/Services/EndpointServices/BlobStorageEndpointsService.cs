using Ardalis.Result;
using ChefKnifeStudios.PokerAttack.Client.Core.Enums;
using ChefKnifeStudios.PokerAttack.Client.Core.Extensions;
using ChefKnifeStudios.PokerAttack.Shared.DTOs.BlobStorage;
using Microsoft.Extensions.Logging;

using Endpoints = ChefKnifeStudios.PokerAttack.Shared.PokerAttackApiEndpoints.BlobStorage;

namespace ChefKnifeStudios.PokerAttack.Client.Core.Services.EndpointServices;

public interface IBlobStorageEndpointsService
{
    Task<Result<UploadRecordingResDTO>> UploadRecordingAsync(UploadRecordingReqDTO reqBody, CancellationToken cancellationToken = default);
    Task<Result<GenerateSasTokenResDTO>> GenerateSasTokenAsync(string containerName, string blobPath, int durationInMinutes = 5, CancellationToken cancellationToken = default);
}

public class BlobStorageEndpointsService : IBlobStorageEndpointsService
{
    readonly ILogger<BlobStorageEndpointsService> _logger;
    readonly IHttpService _httpService;

    public BlobStorageEndpointsService(
        ILogger<BlobStorageEndpointsService> logger,
        IHttpServiceFactory httpServiceFactory)
    {
        _logger = logger;
        _httpService = httpServiceFactory.Create(nameof(APIs.PokerAttackAPI));
    }

    public async Task<Result<UploadRecordingResDTO>> UploadRecordingAsync(UploadRecordingReqDTO reqBody, CancellationToken cancellationToken = default)
    {
        try
        {
            var res = await _httpService.PostAsync<UploadRecordingReqDTO, UploadRecordingResDTO>(
                Endpoints.UploadRecording,
                reqBody,
                cancellationToken
            );
            return res.LogErrors(_logger, "BlobStorage UploadRecordingAsync call");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "An error occured.");
            return Result.Error();
        }
    }

    public async Task<Result<GenerateSasTokenResDTO>> GenerateSasTokenAsync(string containerName, string blobPath, int durationInMinutes = 5, CancellationToken cancellationToken = default)
    {
        try
        {
            // Build URL manually since FormatRoute doesn't handle catch-all {*blobPath} parameters
            var url = $"/blob-storage/sas-token/{Uri.EscapeDataString(containerName)}/{blobPath}?duration={durationInMinutes}";

            var res = await _httpService.GetAsync<GenerateSasTokenResDTO>(
                url,
                cancellationToken
            );
            return res.LogErrors(_logger, "BlobStorage GenerateSasTokenAsync call");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "An error occured.");
            return Result.Error();
        }
    }
}
