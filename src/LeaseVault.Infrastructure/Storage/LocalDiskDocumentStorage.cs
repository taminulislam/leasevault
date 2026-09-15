using System.Security.Cryptography;
using LeaseVault.Core.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LeaseVault.Infrastructure.Storage;

/// <summary>
/// Development / on-premises fallback that lays files out under a root folder using the same
/// keys as the blob implementation. Content is streamed through a SHA-256 hash while writing.
/// </summary>
public sealed class LocalDiskDocumentStorage : IDocumentStorage
{
    private readonly string _root;
    private readonly ILogger<LocalDiskDocumentStorage> _logger;

    public LocalDiskDocumentStorage(IOptions<StorageOptions> options, ILogger<LocalDiskDocumentStorage> logger)
        : this(options.Value.LocalPath, logger)
    {
    }

    public LocalDiskDocumentStorage(string rootPath, ILogger<LocalDiskDocumentStorage> logger)
    {
        _root = Path.GetFullPath(rootPath);
        _logger = logger;
        Directory.CreateDirectory(_root);
    }

    public string ProviderName => "LocalDisk";

    public string RootPath => _root;

    public async Task<StoredObject> UploadAsync(string key, Stream content, string contentType, CancellationToken ct = default)
    {
        var path = Resolve(key);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        var temp = path + ".uploading";
        long size;
        string hash;

        await using (var file = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true))
        using (var sha = SHA256.Create())
        {
            var buffer = new byte[81920];
            size = 0;
            int read;
            while ((read = await content.ReadAsync(buffer, ct)) > 0)
            {
                await file.WriteAsync(buffer.AsMemory(0, read), ct);
                sha.TransformBlock(buffer, 0, read, null, 0);
                size += read;
            }

            sha.TransformFinalBlock([], 0, 0);
            hash = Convert.ToHexString(sha.Hash!).ToLowerInvariant();
        }

        File.Move(temp, path, overwrite: true);
        _logger.LogDebug("Stored {Key} ({Size} bytes) on local disk", key, size);
        return new StoredObject(key, size, hash);
    }

    public Task<Stream> OpenReadAsync(string key, CancellationToken ct = default)
    {
        var path = Resolve(key);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("Stored object not found.", key);
        }

        Stream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);
        return Task.FromResult(stream);
    }

    public Task<bool> ExistsAsync(string key, CancellationToken ct = default) => Task.FromResult(File.Exists(Resolve(key)));

    public Task DeleteAsync(string key, CancellationToken ct = default)
    {
        var path = Resolve(key);
        if (File.Exists(path))
        {
            File.Delete(path);
        }

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<StoredObjectInfo>> ListAsync(string prefix, CancellationToken ct = default)
    {
        var dir = Resolve(prefix);
        if (!Directory.Exists(dir))
        {
            return Task.FromResult<IReadOnlyList<StoredObjectInfo>>([]);
        }

        var items = Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories)
            .Where(f => !f.EndsWith(".uploading", StringComparison.Ordinal))
            .Select(f => new FileInfo(f))
            .OrderBy(f => f.Name)
            .Select(f => new StoredObjectInfo(
                Path.GetRelativePath(_root, f.FullName).Replace('\\', '/'),
                f.Length,
                f.LastWriteTimeUtc,
                null))
            .ToList();

        return Task.FromResult<IReadOnlyList<StoredObjectInfo>>(items);
    }

    private string Resolve(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new ArgumentException("Key required.", nameof(key));
        }

        var full = Path.GetFullPath(Path.Combine(_root, key.Replace('/', Path.DirectorySeparatorChar)));
        if (!full.StartsWith(_root, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Key escapes the storage root.", nameof(key));
        }

        return full;
    }
}
