using LeaseVault.Core.Abstractions;
using LeaseVault.Core.Domain;
using LeaseVault.Core.Retention;
using LeaseVault.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace LeaseVault.Infrastructure.Services;

public sealed record RetentionSweepResult(int Archived, int Disposed);

/// <summary>
/// Applies retention policies: archives or disposes of documents whose retention period has
/// elapsed. Disposal removes every stored version from the content store.
/// </summary>
public sealed class RetentionService
{
    private readonly LeaseVaultDbContext _db;
    private readonly IDocumentStorage _storage;
    private readonly IAuditLog _audit;
    private readonly IClock _clock;
    private readonly ILogger<RetentionService> _logger;

    public RetentionService(LeaseVaultDbContext db, IDocumentStorage storage, IAuditLog audit, IClock clock, ILogger<RetentionService> logger)
    {
        _db = db;
        _storage = storage;
        _audit = audit;
        _clock = clock;
        _logger = logger;
    }

    public async Task<RetentionSweepResult> SweepAsync(CancellationToken ct = default)
    {
        var today = DateOnly.FromDateTime(_clock.UtcNow);
        var policies = await _db.RetentionPolicies.AsNoTracking().ToListAsync(ct);

        var candidates = await _db.Documents
            .Include(d => d.Lease)
            .Include(d => d.RetentionPolicy)
            .Include(d => d.Versions)
            .Where(d => d.Status != DocumentStatus.Disposed)
            .ToListAsync(ct);

        int archived = 0, disposed = 0;
        foreach (var document in candidates)
        {
            var policy = RetentionCalculator.ResolvePolicy(document, policies);
            if (policy is null || !RetentionCalculator.IsDue(document, policy, today))
            {
                continue;
            }

            if (policy.Action == DisposalAction.Archive)
            {
                document.Archive();
                archived++;
                await _audit.AppendAsync(AuditActions.Archived, nameof(Document), document.Id, $"Retention policy '{policy.Name}'", ct);
            }
            else
            {
                foreach (var version in document.Versions)
                {
                    await _storage.DeleteAsync(version.StorageKey, ct);
                }

                document.Dispose(_clock.UtcNow);
                disposed++;
                await _audit.AppendAsync(AuditActions.Disposed, nameof(Document), document.Id, $"Retention policy '{policy.Name}'; {document.Versions.Count} version file(s) deleted", ct);
            }
        }

        if (archived + disposed > 0)
        {
            await _db.SaveChangesAsync(ct);
            _logger.LogInformation("Retention sweep archived {Archived} and disposed {Disposed} document(s)", archived, disposed);
        }

        return new RetentionSweepResult(archived, disposed);
    }
}
