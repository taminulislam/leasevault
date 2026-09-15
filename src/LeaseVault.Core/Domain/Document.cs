using System.ComponentModel.DataAnnotations;

namespace LeaseVault.Core.Domain;

/// <summary>
/// The main aggregate: a logical document with a version history, check-out lock, tags and workflow.
/// </summary>
public class Document
{
    public int Id { get; set; }

    [Required, MaxLength(200)]
    public string Title { get; set; } = string.Empty;

    [MaxLength(2000)]
    public string? Description { get; set; }

    public DocumentCategory Category { get; set; } = DocumentCategory.Other;

    public DocumentStatus Status { get; set; } = DocumentStatus.Draft;

    public int? PropertyId { get; set; }
    public Property? Property { get; set; }

    public int? TenantId { get; set; }
    public Tenant? Tenant { get; set; }

    public int? LeaseId { get; set; }
    public Lease? Lease { get; set; }

    public int? RetentionPolicyId { get; set; }
    public RetentionPolicy? RetentionPolicy { get; set; }

    /// <summary>Highest committed version number (0 = no content yet).</summary>
    public int CurrentVersion { get; set; }

    [MaxLength(200)]
    public string? CheckedOutBy { get; set; }

    public DateTime? CheckedOutUtc { get; set; }

    /// <summary>
    /// Space-separated copy of <see cref="Tags"/> kept on the row so full-text indexes
    /// (SQL Server CONTAINS / SQLite FTS5) can index tags without joins.
    /// </summary>
    [MaxLength(1000)]
    public string TagsText { get; set; } = string.Empty;

    [Required, MaxLength(200)]
    public string CreatedBy { get; set; } = string.Empty;

    public DateTime CreatedUtc { get; set; }

    public DateTime? DisposedUtc { get; set; }

    public ICollection<DocumentTag> Tags { get; set; } = new List<DocumentTag>();
    public ICollection<DocumentVersion> Versions { get; set; } = new List<DocumentVersion>();
    public ICollection<ApprovalWorkflow> Workflows { get; set; } = new List<ApprovalWorkflow>();

    public bool IsCheckedOut => !string.IsNullOrEmpty(CheckedOutBy);

    public bool IsLockedFor(string user) =>
        IsCheckedOut && !string.Equals(CheckedOutBy, user, StringComparison.OrdinalIgnoreCase);

    public DocumentVersion? LatestVersion =>
        Versions.OrderByDescending(v => v.VersionNumber).FirstOrDefault();

    // ---- Check-out / check-in ---------------------------------------------------------------

    public void CheckOut(string user, DateTime nowUtc)
    {
        Guard.NotEmpty(user, nameof(user));
        EnsureEditable();

        if (IsCheckedOut)
        {
            if (IsLockedFor(user))
            {
                throw new DocumentLockedException(CheckedOutBy!);
            }

            // Same user re-checking out: idempotent, refresh timestamp.
            CheckedOutUtc = nowUtc;
            return;
        }

        CheckedOutBy = user;
        CheckedOutUtc = nowUtc;
    }

    /// <summary>
    /// Releases the lock. Only the lock owner may check in unless <paramref name="force"/> is set
    /// (administrators may break another user's lock).
    /// </summary>
    public void CheckIn(string user, bool force = false)
    {
        Guard.NotEmpty(user, nameof(user));

        if (!IsCheckedOut)
        {
            throw new DomainException("Document is not checked out.");
        }

        if (IsLockedFor(user) && !force)
        {
            throw new DocumentLockedException(CheckedOutBy!);
        }

        CheckedOutBy = null;
        CheckedOutUtc = null;
    }

    // ---- Versions --------------------------------------------------------------------------

    /// <summary>
    /// Adds a new immutable version. The uploader must either hold the check-out lock or the
    /// document must be unlocked; versions cannot be added to archived/disposed documents.
    /// </summary>
    public DocumentVersion AddVersion(
        string uploadedBy,
        string fileName,
        string contentType,
        long sizeBytes,
        string storageKey,
        string sha256,
        DateTime nowUtc,
        string? comment = null)
    {
        Guard.NotEmpty(fileName, nameof(fileName));
        Guard.NotEmpty(storageKey, nameof(storageKey));
        EnsureCanAddVersion(uploadedBy);

        if (sizeBytes <= 0)
        {
            throw new DomainException("Uploaded content is empty.");
        }

        var latest = LatestVersion;
        if (latest is not null && string.Equals(latest.Sha256, sha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new DomainException("Uploaded content is identical to the current version.");
        }

        var next = NextVersionNumber;
        var version = new DocumentVersion
        {
            DocumentId = Id,
            VersionNumber = next,
            FileName = fileName,
            ContentType = contentType,
            SizeBytes = sizeBytes,
            StorageKey = storageKey,
            Sha256 = sha256,
            UploadedBy = uploadedBy,
            UploadedUtc = nowUtc,
            Comment = comment
        };

        Versions.Add(version);
        CurrentVersion = next;

        // A new revision invalidates a previous approval: it must be re-approved.
        if (Status is DocumentStatus.Approved or DocumentStatus.Rejected)
        {
            Status = DocumentStatus.Draft;
        }

        return version;
    }

    /// <summary>Fails fast (before any bytes are uploaded) when the user may not add a version.</summary>
    public void EnsureCanAddVersion(string user)
    {
        Guard.NotEmpty(user, nameof(user));
        EnsureEditable();

        if (IsLockedFor(user))
        {
            throw new DocumentLockedException(CheckedOutBy!);
        }
    }

    public int NextVersionNumber => Math.Max(CurrentVersion, LatestVersion?.VersionNumber ?? 0) + 1;

    // ---- Tags ------------------------------------------------------------------------------

    public void SetTags(IEnumerable<string> tags)
    {
        var normalised = tags
            .Select(t => t.Trim().ToLowerInvariant())
            .Where(t => t.Length > 0)
            .Distinct()
            .OrderBy(t => t)
            .ToList();

        Tags.Clear();
        foreach (var t in normalised)
        {
            Tags.Add(new DocumentTag { DocumentId = Id, Value = t });
        }

        TagsText = string.Join(' ', normalised);
    }

    public IReadOnlyList<string> TagValues => Tags.Select(t => t.Value).OrderBy(t => t).ToList();

    // ---- Lifecycle -------------------------------------------------------------------------

    public void Archive()
    {
        if (Status == DocumentStatus.Disposed)
        {
            throw new DomainException("Disposed documents cannot be archived.");
        }

        if (IsCheckedOut)
        {
            throw new DomainException("Check the document in before archiving it.");
        }

        Status = DocumentStatus.Archived;
    }

    public void Dispose(DateTime nowUtc)
    {
        if (IsCheckedOut)
        {
            throw new DomainException("Check the document in before disposing of it.");
        }

        Status = DocumentStatus.Disposed;
        DisposedUtc = nowUtc;
    }

    private void EnsureEditable()
    {
        if (Status is DocumentStatus.Archived or DocumentStatus.Disposed)
        {
            throw new DomainException($"Document is {Status} and can no longer be modified.");
        }
    }
}
