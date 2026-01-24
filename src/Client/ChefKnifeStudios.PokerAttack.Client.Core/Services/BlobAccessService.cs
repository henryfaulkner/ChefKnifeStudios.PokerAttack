using Ardalis.Result;
using ChefKnifeStudios.PokerAttack.Client.Core.Services.EndpointServices;
using Microsoft.Extensions.Logging;

namespace ChefKnifeStudios.PokerAttack.Client.Core.Services;

/// <summary>
/// High-level service for accessing Azure Blob Storage using the Token Exchange Flow.
/// Fetches a short-lived SAS token from the server, then accesses blobs directly from Azure.
/// </summary>
public interface IBlobAccessService
{
    /// <summary>
    /// Gets a SAS URL for direct blob access (for use in img/video src, or direct download).
    /// </summary>
    Task<Result<string>> GetBlobUrlWithSasAsync(string containerName, string blobPath, int durationInMinutes = 5, CancellationToken cancellationToken = default);

    /// <summary>
    /// Downloads blob data using the Token Exchange Flow.
    /// First fetches SAS token from server, then downloads directly from Azure.
    /// </summary>
    Task<Result<byte[]>> DownloadBlobAsync(string containerName, string blobPath, CancellationToken cancellationToken = default);
}

public class BlobAccessService : IBlobAccessService
{
    private readonly IBlobStorageEndpointsService _blobStorageEndpointsService;
    private readonly HttpClient _httpClient;
    private readonly ILogger<BlobAccessService> _logger;

    public BlobAccessService(
        IBlobStorageEndpointsService blobStorageEndpointsService,
        HttpClient httpClient,
        ILogger<BlobAccessService> logger)
    {
        _blobStorageEndpointsService = blobStorageEndpointsService;
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<Result<string>> GetBlobUrlWithSasAsync(string containerName, string blobPath, int durationInMinutes = 5, CancellationToken cancellationToken = default)
    {
        try
        {
            var tokenResult = await _blobStorageEndpointsService.GenerateSasTokenAsync(
                containerName,
                blobPath,
                durationInMinutes,
                cancellationToken
            );

            if (!tokenResult.IsSuccess)
            {
                _logger.LogError("Failed to generate SAS token for {ContainerName}/{BlobPath}", containerName, blobPath);
                return Result.Error("Failed to generate SAS token");
            }

            return Result.Success(tokenResult.Value.SasUrl);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting SAS URL for {ContainerName}/{BlobPath}", containerName, blobPath);
            return Result.Error("Error getting SAS URL");
        }
    }

    public async Task<Result<byte[]>> DownloadBlobAsync(string containerName, string blobPath, CancellationToken cancellationToken = default)
    {
        try
        {
            // Step 1: Get SAS URL from server
            var sasUrlResult = await GetBlobUrlWithSasAsync(containerName, blobPath, 5, cancellationToken);

            if (!sasUrlResult.IsSuccess)
            {
                return Result.Error(new Ardalis.Result.ErrorList(sasUrlResult.Errors.ToList()));
            }

            // Step 2: Download directly from Azure using the SAS URL
            _logger.LogInformation("Downloading blob from Azure: {ContainerName}/{BlobPath}", containerName, blobPath);

            var response = await _httpClient.GetAsync(sasUrlResult.Value, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("Failed to download blob from Azure. Status: {StatusCode}", response.StatusCode);
                return Result.Error($"Failed to download blob. Status: {response.StatusCode}");
            }

            var data = await response.Content.ReadAsByteArrayAsync(cancellationToken);

            _logger.LogInformation("Successfully downloaded {ByteCount} bytes from {ContainerName}/{BlobPath}", data.Length, containerName, blobPath);

            return Result.Success(data);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error downloading blob {ContainerName}/{BlobPath}", containerName, blobPath);
            return Result.Error("Error downloading blob");
        }
    }
}
