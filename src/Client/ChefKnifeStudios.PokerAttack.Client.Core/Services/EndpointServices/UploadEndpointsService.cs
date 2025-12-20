using Ardalis.Result;
using ChefKnifeStudios.PokerAttack.Client.Core.Enums;
using ChefKnifeStudios.PokerAttack.Client.Core.Extensions;
using ChefKnifeStudios.PokerAttack.Shared.DTOs.Upload;
using Microsoft.Extensions.Logging;

using Endpoints = ChefKnifeStudios.PokerAttack.Shared.PokerAttackApiEndpoints.Upload;

namespace ChefKnifeStudios.PokerAttack.Client.Core.Services.EndpointServices;

public interface IUploadEndpointsService
{
    Task<Result<UploadRecordingResDTO>> PublishRecordingAsync(UploadRecordingReqDTO reqBody, CancellationToken cancellationToken = default);
}

public class UploadEndpointsService : IUploadEndpointsService
{
    readonly ILogger<UploadEndpointsService> _logger;
    readonly IHttpService _httpService;

    public UploadEndpointsService(
        ILogger<UploadEndpointsService> logger,
        IHttpServiceFactory httpServiceFactory)
    {
        _logger = logger;
        _httpService = httpServiceFactory.Create(nameof(APIs.PokerAttackAPI));
    }

    public async Task<Result<UploadRecordingResDTO>> PublishRecordingAsync(UploadRecordingReqDTO reqBody, CancellationToken cancellationToken = default)
    {
        try
        {
            var res = await _httpService.PostAsync<UploadRecordingReqDTO, UploadRecordingResDTO>(
                Endpoints.PublishRecording,
                reqBody,
                cancellationToken
            );
            return res.LogErrors(_logger, "Upload PublishRecordingAsync call");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "An error occured.");
            return Result.Error();
        }
    }
}
