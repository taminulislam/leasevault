namespace LeaseVault.Core.Abstractions;

/// <summary>
/// Content store for document versions. Implementations: Azure Blob Storage (block upload,
/// version listing) and a local file-system fallback for development.
/// </summary>
public interface IDocumentStorage
{
    /// <summary>Human-readable provider name used in diagnostics and the UI.</summary>
    string ProviderName { get; }

    /// <summary>
    /// Streams <paramref name="content"/> into the store under <paramref name="key"/>. Large
    /// streams are uploaded in blocks/chunks so memory usage stays flat. Returns the SHA-256 of
    /// the stored bytes and the byte count.
    /// </summary>
    Task<StoredObject> UploadAsync(string key, Stream content, string contentType, CancellationToken ct = default);

    /// <summary>Opens the stored content for reading. Throws <see cref="FileNotFoundException"/> when missing.</summary>
    Task<Stream> OpenReadAsync(string key, CancellationToken ct = default);

    Task<bool> ExistsAsync(string key, CancellationToken ct = default);

    Task DeleteAsync(string key, CancellationToken ct = default);

    /// <summary>Lists every stored object whose key starts with <paramref name="prefix"/> (a document's version files).</summary>
    Task<IReadOnlyList<StoredObjectInfo>> ListAsync(string prefix, CancellationToken ct = default);
}

public sealed record StoredObject(string Key, long SizeBytes, string Sha256);

public sealed record StoredObjectInfo(string Key, long SizeBytes, DateTimeOffset? LastModified, string? VersionId);

/// <summary>Builds deterministic storage keys so both providers lay files out identically.</summary>
public static class StorageKeys
{
    public static string ForVersion(int documentId, int versionNumber, string fileName)
    {
        var ext = Path.GetExtension(fileName);
        return $"documents/{documentId:D8}/v{versionNumber:D4}{ext}";
    }

    public static string PrefixForDocument(int documentId) => $"documents/{documentId:D8}/";
}
