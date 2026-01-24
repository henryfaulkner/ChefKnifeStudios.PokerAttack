using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Sas;
using Azure.Identity;
using Microsoft.Extensions.Logging;

namespace ChefKnifeStudios.PokerAttack.Server.BL.Services;

public interface IStorageService
{
    Task<string> StoreAsync(string containerName, string[] directoryNames, string filename, MemoryStream dataToStore, CancellationToken cancellationToken = default);
    Task<string> StoreAsync(string containerName, string[] directoryNames, string filename, byte[] dataToStore, CancellationToken cancellationToken = default);
    Task<byte[]> DownloadAsync(string url, string containerName, CancellationToken cancellationToken = default);
    Task<string> GenerateUserDelegationSasAsync(string containerName, string blobName, int durationInMinutes = 60, CancellationToken cancellationToken = default);
}

public class BlobStorageService : IStorageService
{
    readonly BlobServiceClient _blobServiceClient;
    readonly ILogger<BlobStorageService> _logger;

    // Cached User Delegation Key for fast SAS token generation
    UserDelegationKey? _cachedUserDelegationKey;
    DateTimeOffset _keyExpiresOn = DateTimeOffset.MinValue;
    readonly SemaphoreSlim _keyLock = new(1, 1);
    const int KEY_DURATION_MINUTES = 60;
    const int KEY_REFRESH_BUFFER_MINUTES = 5;

    public BlobStorageService(string storageAccountUrl, ILogger<BlobStorageService> logger)
    {
        _blobServiceClient = new BlobServiceClient(
            new Uri(storageAccountUrl),
            new DefaultAzureCredential()
        );
        _logger = logger;
    }

    /// <summary>
    /// Downloads blob from Azure Blob Storage
    /// </summary>
    /// <param name="url">The blob URL</param>
    /// <param name="containerName">The container name</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Blob content as byte array</returns>
    public async Task<byte[]> DownloadAsync(string url, string containerName, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Starting DownloadAsync for URL: {Url}", url);
        try
        {
            var containerClient = _blobServiceClient.GetBlobContainerClient(containerName);

            // Extract blob name from URL
            var uri = new Uri(url);
            var blobName = uri.AbsolutePath.Split(new[] { $"/{containerName}/" }, StringSplitOptions.None).Last();

            var blobClient = containerClient.GetBlobClient(blobName);

            using var memoryStream = new MemoryStream();
            await blobClient.DownloadToAsync(memoryStream, cancellationToken);

            _logger.LogInformation("Successfully downloaded blob from URL: {Url}", url);
            return memoryStream.ToArray();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception occurred in DownloadAsync for URL: {Url}", url);
            throw;
        }
    }

    /// <summary>
    /// Uploads MemoryStream data to Azure Blob Storage
    /// </summary>
    /// <param name="containerName">The container name (equivalent to share name)</param>
    /// <param name="directoryNames">Virtual directory path segments</param>
    /// <param name="filename">The blob filename</param>
    /// <param name="dataToStore">Data to upload</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The blob URI</returns>
    public async Task<string> StoreAsync(string containerName, string[] directoryNames, string filename, MemoryStream dataToStore, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Starting StoreAsync (MemoryStream) for container: {ContainerName}, filename: {Filename}", containerName, filename);
        try
        {
            var blobUri = await UploadToBlobAsync(containerName, directoryNames, filename, dataToStore, cancellationToken);
            _logger.LogInformation("Successfully stored blob at: {BlobUri}", blobUri);
            return blobUri;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception occurred in StoreAsync (MemoryStream) for container: {ContainerName}, filename: {Filename}", containerName, filename);
            throw;
        }
    }

    /// <summary>
    /// Uploads byte array data to Azure Blob Storage
    /// </summary>
    /// <param name="containerName">The container name (equivalent to share name)</param>
    /// <param name="directoryNames">Virtual directory path segments</param>
    /// <param name="filename">The blob filename</param>
    /// <param name="dataToStore">Data to upload</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The blob URI</returns>
    public async Task<string> StoreAsync(string containerName, string[] directoryNames, string filename, byte[] dataToStore, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Starting StoreAsync (byte[]) for container: {ContainerName}, filename: {Filename}", containerName, filename);
        try
        {
            using var streamedData = new MemoryStream(dataToStore);
            var blobUri = await UploadToBlobAsync(containerName, directoryNames, filename, streamedData, cancellationToken);
            _logger.LogInformation("Successfully stored blob at: {BlobUri}", blobUri);
            return blobUri;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception occurred in StoreAsync (byte[]) for container: {ContainerName}, filename: {Filename}", containerName, filename);
            throw;
        }
    }

    /// <summary>
    /// Generates a User Delegation SAS for the client-side WASM app.
    /// This is the core of the "Token Exchange" flow.
    /// Uses a cached User Delegation Key for fast, local cryptographic signing.
    /// </summary>
    public async Task<string> GenerateUserDelegationSasAsync(string containerName, string blobName, int durationInMinutes = 5, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Generating User Delegation SAS for blob: {BlobName} in container: {ContainerName}", blobName, containerName);

        try
        {
            // 1. Get or refresh the cached User Delegation Key
            var userDelegationKey = await GetOrRefreshUserDelegationKeyAsync(cancellationToken);

            // 2. Define the SAS permissions and constraints
            var sasBuilder = new BlobSasBuilder
            {
                BlobContainerName = containerName,
                BlobName = blobName,
                Resource = "b", // "b" for blob level access
                StartsOn = DateTimeOffset.UtcNow,
                ExpiresOn = DateTimeOffset.UtcNow.AddMinutes(durationInMinutes)
            };

            // Allow read access for the client
            sasBuilder.SetPermissions(BlobSasPermissions.Read);

            // 3. Construct the full URI with the SAS token (fast, local crypto operation)
            var blobClient = _blobServiceClient.GetBlobContainerClient(containerName).GetBlobClient(blobName);
            var sasUri = new BlobUriBuilder(blobClient.Uri)
            {
                // Sign the SAS with the cached User Delegation Key
                Sas = sasBuilder.ToSasQueryParameters(userDelegationKey, _blobServiceClient.AccountName)
            }.ToUri();

            return sasUri.ToString();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to generate User Delegation SAS for {BlobName}", blobName);
            throw;
        }
    }

    /// <summary>
    /// Gets the cached User Delegation Key or refreshes it if expired/near expiration.
    /// This avoids a network call to Azure for each SAS token generation.
    /// </summary>
    private async Task<Azure.Storage.Blobs.Models.UserDelegationKey> GetOrRefreshUserDelegationKeyAsync(CancellationToken cancellationToken)
    {
        // Check if key is still valid (with buffer time)
        if (_cachedUserDelegationKey is not null &&
            DateTimeOffset.UtcNow.AddMinutes(KEY_REFRESH_BUFFER_MINUTES) < _keyExpiresOn)
        {
            return _cachedUserDelegationKey;
        }

        // Need to refresh - use lock to prevent multiple concurrent refreshes
        await _keyLock.WaitAsync(cancellationToken);
        try
        {
            // Double-check after acquiring lock
            if (_cachedUserDelegationKey is not null &&
                DateTimeOffset.UtcNow.AddMinutes(KEY_REFRESH_BUFFER_MINUTES) < _keyExpiresOn)
            {
                return _cachedUserDelegationKey;
            }

            _logger.LogInformation("Refreshing User Delegation Key (valid for {Duration} minutes)", KEY_DURATION_MINUTES);

            var keyStartsOn = DateTimeOffset.UtcNow;
            var keyExpiresOn = keyStartsOn.AddMinutes(KEY_DURATION_MINUTES);

            var response = await _blobServiceClient.GetUserDelegationKeyAsync(
                keyStartsOn,
                keyExpiresOn,
                cancellationToken);

            _cachedUserDelegationKey = response.Value;
            _keyExpiresOn = keyExpiresOn;

            _logger.LogInformation("User Delegation Key refreshed, expires at {ExpiresOn}", _keyExpiresOn);

            return _cachedUserDelegationKey;
        }
        finally
        {
            _keyLock.Release();
        }
    }

    /// <summary>
    /// Uploads data to Azure Blob Storage with virtual directory structure
    /// </summary>
    async Task<string> UploadToBlobAsync(string containerName, string[] directoryNames, string filename, MemoryStream dataToStore, CancellationToken cancellationToken)
    {
        // Get or create container
        var containerClient = _blobServiceClient.GetBlobContainerClient(containerName);
        await containerClient.CreateIfNotExistsAsync(PublicAccessType.None, cancellationToken: cancellationToken);

        // Build blob name with virtual directory path
        // Example: directoryNames = ["dir1", "dir2"], filename = "file.txt" → "dir1/dir2/file.txt"
        var blobName = directoryNames.Length > 0
            ? string.Join("/", directoryNames) + "/" + filename
            : filename;

        // Get blob client and upload
        var blobClient = containerClient.GetBlobClient(blobName);

        // Reset stream position before upload
        dataToStore.Position = 0;

        await blobClient.UploadAsync(dataToStore, overwrite: true, cancellationToken);

        return blobClient.Uri.AbsoluteUri;
    }
}