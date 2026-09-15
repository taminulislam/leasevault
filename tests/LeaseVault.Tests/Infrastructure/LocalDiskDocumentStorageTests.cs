using System.Security.Cryptography;
using System.Text;
using LeaseVault.Core.Abstractions;
using LeaseVault.Infrastructure.Storage;
using LeaseVault.Tests.Support;

namespace LeaseVault.Tests.Infrastructure;

public class LocalDiskDocumentStorageTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "leasevault-tests", Guid.NewGuid().ToString("N"));
    private readonly LocalDiskDocumentStorage _storage;

    public LocalDiskDocumentStorageTests()
    {
        _storage = new LocalDiskDocumentStorage(_root, TestData.Logger<LocalDiskDocumentStorage>());
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Fact]
    public async Task Upload_streams_content_and_returns_size_and_sha256()
    {
        var bytes = Encoding.UTF8.GetBytes("LeaseVault content " + new string('x', 200_000));
        var key = StorageKeys.ForVersion(7, 2, "lease.pdf");

        StoredObject stored;
        using (var input = new MemoryStream(bytes))
        {
            stored = await _storage.UploadAsync(key, input, "application/pdf");
        }

        Assert.Equal(bytes.Length, stored.SizeBytes);
        Assert.Equal(Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(), stored.Sha256);
        Assert.True(await _storage.ExistsAsync(key));

        await using var read = await _storage.OpenReadAsync(key);
        using var buffer = new MemoryStream();
        await read.CopyToAsync(buffer);
        Assert.Equal(bytes, buffer.ToArray());
    }

    [Fact]
    public async Task List_returns_every_version_under_the_document_prefix()
    {
        foreach (var v in new[] { 1, 2, 3 })
        {
            using var input = new MemoryStream(Encoding.UTF8.GetBytes($"v{v}"));
            await _storage.UploadAsync(StorageKeys.ForVersion(42, v, "lease.docx"), input, "application/octet-stream");
        }

        using var other = new MemoryStream([1, 2, 3]);
        await _storage.UploadAsync(StorageKeys.ForVersion(43, 1, "other.pdf"), other, "application/pdf");

        var versions = await _storage.ListAsync(StorageKeys.PrefixForDocument(42));

        Assert.Equal(["documents/00000042/v0001.docx", "documents/00000042/v0002.docx", "documents/00000042/v0003.docx"], versions.Select(v => v.Key));
    }

    [Fact]
    public async Task Delete_removes_object_and_missing_reads_throw()
    {
        var key = StorageKeys.ForVersion(1, 1, "a.txt");
        using var input = new MemoryStream(Encoding.UTF8.GetBytes("bye"));
        await _storage.UploadAsync(key, input, "text/plain");

        await _storage.DeleteAsync(key);

        Assert.False(await _storage.ExistsAsync(key));
        await Assert.ThrowsAsync<FileNotFoundException>(() => _storage.OpenReadAsync(key));
    }

    [Fact]
    public async Task Keys_cannot_escape_the_storage_root()
    {
        using var input = new MemoryStream([1]);
        await Assert.ThrowsAsync<ArgumentException>(() => _storage.UploadAsync("../../evil.txt", input, "text/plain"));
    }
}
