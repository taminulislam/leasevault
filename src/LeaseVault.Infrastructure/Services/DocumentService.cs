using LeaseVault.Core.Abstractions;
using LeaseVault.Core.Domain;
using LeaseVault.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace LeaseVault.Infrastructure.Services;

public sealed record NewDocumentRequest(
    string Title,
    string? Description,
    DocumentCategory Category,
    int? PropertyId,
    int? TenantId,
    int? LeaseId,
    int? RetentionPolicyId,
    IEnumerable<string> Tags);

/// <summary>
/// Application service for the Document aggregate: orchestrates the domain rules, content
/// storage and audit trail. All rule checks live in <see cref="Document"/>.
/// </summary>
public sealed class DocumentService
{
    private readonly LeaseVaultDbContext _db;
    private readonly IDocumentStorage _storage;
    private readonly IAuditLog _audit;
    private readonly ICurrentUser _user;
    private readonly IClock _clock;
    private readonly ILogger<DocumentService> _logger;

    public DocumentService(
        LeaseVaultDbContext db,
        IDocumentStorage storage,
        IAuditLog audit,
        ICurrentUser user,
        IClock clock,
        ILogger<DocumentService> logger)
    {
        _db = db;
        _storage = storage;
        _audit = audit;
        _user = user;
        _clock = clock;
        _logger = logger;
    }

    public async Task<Document> CreateAsync(NewDocumentRequest request, CancellationToken ct = default)
    {
        var document = new Document
        {
            Title = request.Title.Trim(),
            Description = request.Description?.Trim(),
            Category = request.Category,
            PropertyId = request.PropertyId,
            TenantId = request.TenantId,
            LeaseId = request.LeaseId,
            RetentionPolicyId = request.RetentionPolicyId,
            CreatedBy = _user.Name,
            CreatedUtc = _clock.UtcNow
        };
        document.SetTags(request.Tags);

        _db.Documents.Add(document);
        await _db.SaveChangesAsync(ct);

        await _audit.AppendAsync(AuditActions.Created, nameof(Document), document.Id, $"Title='{document.Title}'", ct);
        await _db.SaveChangesAsync(ct);
        return document;
    }

    public async Task UpdateMetadataAsync(int id, NewDocumentRequest request, CancellationToken ct = default)
    {
        var document = await LoadAsync(id, ct);
        if (document.IsLockedFor(_user.Name))
        {
            throw new DocumentLockedException(document.CheckedOutBy!);
        }

        document.Title = request.Title.Trim();
        document.Description = request.Description?.Trim();
        document.Category = request.Category;
        document.PropertyId = request.PropertyId;
        document.TenantId = request.TenantId;
        document.LeaseId = request.LeaseId;
        document.RetentionPolicyId = request.RetentionPolicyId;
        document.SetTags(request.Tags);

        await _audit.AppendAsync(AuditActions.Updated, nameof(Document), id, "Metadata updated", ct);
        await _db.SaveChangesAsync(ct);
    }

    public async Task<Document> CheckOutAsync(int id, CancellationToken ct = default)
    {
        var document = await LoadAsync(id, ct);
        document.CheckOut(_user.Name, _clock.UtcNow);
        await _audit.AppendAsync(AuditActions.CheckedOut, nameof(Document), id, null, ct);
        await _db.SaveChangesAsync(ct);
        return document;
    }

    public async Task<Document> CheckInAsync(int id, bool force = false, CancellationToken ct = default)
    {
        var document = await LoadAsync(id, ct);
        var previousOwner = document.CheckedOutBy;
        document.CheckIn(_user.Name, force);
        await _audit.AppendAsync(AuditActions.CheckedIn, nameof(Document), id, force ? $"Lock of '{previousOwner}' broken" : null, ct);
        await _db.SaveChangesAsync(ct);
        return document;
    }

    /// <summary>
    /// Streams a new version into storage (chunked by the provider), then records it. If the
    /// domain rejects the version after upload (e.g. identical content) the uploaded object is removed.
    /// </summary>
    public async Task<DocumentVersion> AddVersionAsync(int id, Stream content, string fileName, string contentType, string? comment, CancellationToken ct = default)
    {
        var document = await LoadAsync(id, ct);
        document.EnsureCanAddVersion(_user.Name);

        var safeName = Path.GetFileName(fileName);
        var key = StorageKeys.ForVersion(document.Id, document.NextVersionNumber, safeName);
        var stored = await _storage.UploadAsync(key, content, contentType, ct);

        DocumentVersion version;
        try
        {
            version = document.AddVersion(_user.Name, safeName, contentType, stored.SizeBytes, key, stored.Sha256, _clock.UtcNow, comment);
        }
        catch (DomainException)
        {
            await _storage.DeleteAsync(key, ct);
            throw;
        }

        await _audit.AppendAsync(AuditActions.VersionAdded, nameof(Document), id, $"v{version.VersionNumber} {safeName} ({stored.SizeBytes} bytes)", ct);
        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("Document {DocumentId} version {Version} stored via {Provider}", id, version.VersionNumber, _storage.ProviderName);
        return version;
    }

    public async Task<(DocumentVersion Version, Stream Content)> OpenVersionAsync(int id, int versionNumber, CancellationToken ct = default)
    {
        var version = await _db.DocumentVersions.AsNoTracking()
            .FirstOrDefaultAsync(v => v.DocumentId == id && v.VersionNumber == versionNumber, ct)
            ?? throw new KeyNotFoundException($"Version {versionNumber} of document {id} not found.");

        var stream = await _storage.OpenReadAsync(version.StorageKey, ct);
        await _audit.AppendAsync(AuditActions.Downloaded, nameof(Document), id, $"v{versionNumber}", ct);
        await _db.SaveChangesAsync(ct);
        return (version, stream);
    }

    public Task<IReadOnlyList<StoredObjectInfo>> ListStoredVersionsAsync(int id, CancellationToken ct = default) =>
        _storage.ListAsync(StorageKeys.PrefixForDocument(id), ct);

    public async Task ArchiveAsync(int id, CancellationToken ct = default)
    {
        var document = await LoadAsync(id, ct);
        document.Archive();
        await _audit.AppendAsync(AuditActions.Archived, nameof(Document), id, null, ct);
        await _db.SaveChangesAsync(ct);
    }

    private async Task<Document> LoadAsync(int id, CancellationToken ct) =>
        await _db.Documents
            .Include(d => d.Versions)
            .Include(d => d.Tags)
            .Include(d => d.Workflows)
            .FirstOrDefaultAsync(d => d.Id == id, ct)
        ?? throw new KeyNotFoundException($"Document {id} not found.");
}
