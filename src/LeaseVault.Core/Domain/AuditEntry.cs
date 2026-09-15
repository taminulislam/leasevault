using System.ComponentModel.DataAnnotations;

namespace LeaseVault.Core.Domain;

/// <summary>
/// Append-only audit record. The persistence layer rejects updates and deletes of these rows.
/// </summary>
public class AuditEntry
{
    public long Id { get; set; }

    public DateTime TimestampUtc { get; set; }

    [Required, MaxLength(200)]
    public string Actor { get; set; } = string.Empty;

    [Required, MaxLength(80)]
    public string Action { get; set; } = string.Empty;

    [Required, MaxLength(80)]
    public string EntityType { get; set; } = string.Empty;

    [MaxLength(40)]
    public string? EntityId { get; set; }

    [MaxLength(4000)]
    public string? Details { get; set; }

    [MaxLength(64)]
    public string? CorrelationId { get; set; }

    public static AuditEntry Create(string actor, string action, string entityType, object? entityId, string? details, DateTime nowUtc, string? correlationId = null) =>
        new()
        {
            Actor = actor,
            Action = action,
            EntityType = entityType,
            EntityId = entityId?.ToString(),
            Details = details,
            TimestampUtc = nowUtc,
            CorrelationId = correlationId
        };
}
