using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.Extensions.Logging;

namespace ChefKnifeStudios.PokerAttack.Server.BL.Services;

public interface IStorageService
{
    Task<string> StoreAsync(string containerName, string[] directoryNames, string filename, MemoryStream dataToStore, CancellationToken cancellationToken = default);
    Task<string> StoreAsync(string containerName, string[] directoryNames, string filename, byte[] dataToStore, CancellationToken cancellationToken = default);
    Task<byte[]> DownloadAsync(string url, string containerName, CancellationToken cancellationToken = default);
}

public class BlobStorageService : IStorageService
{
    private readonly BlobServiceClient _blobServiceClient;
    private readonly ILogger<BlobStorageService> _logger;

    public BlobStorageService(string azureStorageConnectionString, ILogger<BlobStorageService> logger)
    {
        _blobServiceClient = new BlobServiceClient(azureStorageConnectionString);
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

    #region Private Methods

    /// <summary>
    /// Uploads data to Azure Blob Storage with virtual directory structure
    /// </summary>
    private async Task<string> UploadToBlobAsync(string containerName, string[] directoryNames, string filename, MemoryStream dataToStore, CancellationToken cancellationToken)
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

    #endregion
}