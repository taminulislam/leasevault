using System.ComponentModel.DataAnnotations;

namespace LeaseVault.Core.Domain;

/// <summary>
/// How long a category of document must be kept after its trigger event (lease end, or creation
/// for documents not tied to a lease) and what happens afterwards.
/// </summary>
public class RetentionPolicy
{
    public int Id { get; set; }

    [Required, MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    /// <summary>Category this policy applies to by default; null = catch-all.</summary>
    public DocumentCategory? Category { get; set; }

    [Range(0, 100)]
    public int RetentionYears { get; set; } = 7;

    public DisposalAction Action { get; set; } = DisposalAction.Archive;

    /// <summary>Documents under legal hold are never disposed of automatically.</summary>
    public bool LegalHold { get; set; }

    [MaxLength(500)]
    public string? Description { get; set; }

    public ICollection<Document> Documents { get; set; } = new List<Document>();
}
