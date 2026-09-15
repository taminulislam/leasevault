using LeaseVault.Core.Abstractions;
using LeaseVault.Core.Domain;

namespace LeaseVault.Infrastructure.Persistence;

/// <summary>
/// Writes audit rows through the shared DbContext so they commit atomically with the change
/// they describe. Rows are never updated or deleted (enforced by <see cref="LeaseVaultDbContext"/>).
/// </summary>
public sealed class EfAuditLog : IAuditLog
{
    private readonly LeaseVaultDbContext _db;
    private readonly ICurrentUser _user;
    private readonly IClock _clock;

    public EfAuditLog(LeaseVaultDbContext db, ICurrentUser user, IClock clock)
    {
        _db = db;
        _user = user;
        _clock = clock;
    }

    public Task AppendAsync(string action, string entityType, object? entityId, string? details = null, CancellationToken ct = default)
    {
        var entry = AuditEntry.Create(_user.Name, action, entityType, entityId, details, _clock.UtcNow);
        _db.AuditEntries.Add(entry);
        return Task.CompletedTask; // persisted with the caller's SaveChanges
    }
}
