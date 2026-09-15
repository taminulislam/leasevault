using System.ComponentModel.DataAnnotations;

namespace LeaseVault.Core.Domain;

/// <summary>
/// An immutable snapshot of a document's content. Rows are never updated once written.
/// </summary>
public class DocumentVersion
{
    public int Id { get; set; }

    public int DocumentId { get; set; }
    public Document? Document { get; set; }

    public int VersionNumber { get; set; }

    [Required, MaxLength(260)]
    public string FileName { get; set; } = string.Empty;

    [Required, MaxLength(120)]
    public string ContentType { get; set; } = "application/octet-stream";

    public long SizeBytes { get; set; }

    /// <summary>Key/path understood by <c>IDocumentStorage</c> (blob name or relative file path).</summary>
    [Required, MaxLength(400)]
    public string StorageKey { get; set; } = string.Empty;

    [Required, MaxLength(64)]
    public string Sha256 { get; set; } = string.Empty;

    [Required, MaxLength(200)]
    public string UploadedBy { get; set; } = string.Empty;

    public DateTime UploadedUtc { get; set; }

    [MaxLength(500)]
    public string? Comment { get; set; }
}
