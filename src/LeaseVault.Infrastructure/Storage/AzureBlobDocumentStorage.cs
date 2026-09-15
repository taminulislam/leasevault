using System.Security.Cryptography;
using Azure;
using Azure.Identity;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Blobs.Specialized;
using LeaseVault.Core.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LeaseVault.Infrastructure.Storage;

/// <summary>
/// Azure Blob Storage implementation. Uploads are staged as blocks (chunked) and committed with a
/// block list so multi-GB files never sit in memory; blob versioning on the container provides
/// an immutable history that <see cref="ListAsync"/> exposes through <c>VersionId</c>.
/// Authentication uses <see cref="DefaultAzureCredential"/> (system-assigned managed identity on
/// App Service, developer credentials locally) when <c>Storage:AccountUri</c> is configured.
/// </summary>
public sealed class AzureBlobDocumentStorage : IDocumentStorage
{
    private readonly BlobContainerClient _container;
    private readonly int _blockSize;
    private readonly ILogger<AzureBlobDocumentStorage> _logger;
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private bool _initialised;

    public AzureBlobDocumentStorage(IOptions<StorageOptions> options, ILogger<AzureBlobDocumentStorage> logger)
        : this(CreateServiceClient(options.Value), options.Value, logger)
    {
    }

    public AzureBlobDocumentStorage(BlobServiceClient serviceClient, StorageOptions options, ILogger<AzureBlobDocumentStorage> logger)
    {
        _container = serviceClient.GetBlobContainerClient(options.ContainerName);
        _blockSize = Math.Max(256 * 1024, options.BlockSizeBytes);
        _logger = logger;
    }

    public string ProviderName => "AzureBlob";

    private static BlobServiceClient CreateServiceClient(StorageOptions options)
    {
        if (!string.IsNullOrWhiteSpace(options.AccountUri))
        {
            // Managed identity in Azure; VS / az cli / env credentials on a developer machine.
            return new BlobServiceClient(new Uri(options.AccountUri), new DefaultAzureCredential());
        }

        if (!string.IsNullOrWhiteSpace(options.ConnectionString))
        {
            return new BlobServiceClient(options.ConnectionString);
        }

        throw new InvalidOperationException("Storage:AccountUri or Storage:ConnectionString must be configured for Azure Blob storage.");
    }

    public async Task<StoredObject> UploadAsync(string key, Stream content, string contentType, CancellationToken ct = default)
    {
        await EnsureContainerAsync(ct);
        var blob = _container.GetBlockBlobClient(key);

        var blockIds = new List<string>();
        var buffer = new byte[_blockSize];
        long total = 0;
        using var sha = SHA256.Create();

        while (true)
        {
            var filled = await FillBufferAsync(content, buffer, ct);
            if (filled == 0)
            {
                break;
            }

            var blockId = Convert.ToBase64String(BitConverter.GetBytes(blockIds.Count).Concat(new byte[12]).ToArray());
            using var chunk = new MemoryStream(buffer, 0, filled, writable: false);
            await blob.StageBlockAsync(blockId, chunk, cancellationToken: ct);
            blockIds.Add(blockId);

            sha.TransformBlock(buffer, 0, filled, null, 0);
            total += filled;
            _logger.LogDebug("Staged block {Index} ({Bytes} bytes) for {Key}", blockIds.Count, filled, key);
        }

        sha.TransformFinalBlock([], 0, 0);
        var hash = Convert.ToHexString(sha.Hash!).ToLowerInvariant();

        await blob.CommitBlockListAsync(
            blockIds,
            new CommitBlockListOptions
            {
                HttpHeaders = new BlobHttpHeaders { ContentType = contentType },
                Metadata = new Dictionary<string, string> { ["sha256"] = hash }
            },
            ct);

        _logger.LogInformation("Committed {Blocks} block(s), {Bytes} bytes to blob {Key}", blockIds.Count, total, key);
        return new StoredObject(key, total, hash);
    }

    public async Task<Stream> OpenReadAsync(string key, CancellationToken ct = default)
    {
        var blob = _container.GetBlobClient(key);
        try
        {
            return await blob.OpenReadAsync(cancellationToken: ct);
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            throw new FileNotFoundException("Stored object not found.", key, ex);
        }
    }

    public async Task<bool> ExistsAsync(string key, CancellationToken ct = default) =>
        (await _container.GetBlobClient(key).ExistsAsync(ct)).Value;

    public async Task DeleteAsync(string key, CancellationToken ct = default) =>
        await _container.GetBlobClient(key).DeleteIfExistsAsync(DeleteSnapshotsOption.IncludeSnapshots, cancellationToken: ct);

    public async Task<IReadOnlyList<StoredObjectInfo>> ListAsync(string prefix, CancellationToken ct = default)
    {
        await EnsureContainerAsync(ct);
        var items = new List<StoredObjectInfo>();
        await foreach (var item in _container.GetBlobsAsync(BlobTraits.None, BlobStates.Version, prefix, ct))
        {
            items.Add(new StoredObjectInfo(item.Name, item.Properties.ContentLength ?? 0, item.Properties.LastModified, item.VersionId));
        }

        return items;
    }

    private async Task EnsureContainerAsync(CancellationToken ct)
    {
        if (_initialised)
        {
            return;
        }

        await _initLock.WaitAsync(ct);
        try
        {
            if (!_initialised)
            {
                await _container.CreateIfNotExistsAsync(cancellationToken: ct);
                _initialised = true;
            }
        }
        finally
        {
            _initLock.Release();
        }
    }

    private static async Task<int> FillBufferAsync(Stream source, byte[] buffer, CancellationToken ct)
    {
        var offset = 0;
        while (offset < buffer.Length)
        {
            var read = await source.ReadAsync(buffer.AsMemory(offset, buffer.Length - offset), ct);
            if (read == 0)
            {
                break;
            }

            offset += read;
        }

        return offset;
    }
}
