namespace LeaseVault.Infrastructure.Storage;

/// <summary>
/// Bound from the "Storage" configuration section.
/// <list type="bullet">
/// <item><c>AccountUri</c> set  -> Azure Blob Storage with <c>DefaultAzureCredential</c> (managed identity).</item>
/// <item><c>ConnectionString</c> set -> Azure Blob Storage with a connection string (local Azurite).</item>
/// <item>neither -> local file-system under <c>LocalPath</c>.</item>
/// </list>
/// </summary>
public sealed class StorageOptions
{
    public const string SectionName = "Storage";

    /// <summary>e.g. https://stleasevault.blob.core.windows.net</summary>
    public string? AccountUri { get; set; }

    public string? ConnectionString { get; set; }

    public string ContainerName { get; set; } = "documents";

    public string LocalPath { get; set; } = "App_Data/storage";

    /// <summary>Block size used for staged (chunked) blob uploads. Default 4 MiB.</summary>
    public int BlockSizeBytes { get; set; } = 4 * 1024 * 1024;

    public bool UseAzureBlob => !string.IsNullOrWhiteSpace(AccountUri) || !string.IsNullOrWhiteSpace(ConnectionString);
}
