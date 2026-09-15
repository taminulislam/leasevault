using LeaseVault.Core.Domain;
using Microsoft.EntityFrameworkCore;

namespace LeaseVault.Infrastructure.Persistence;

public class LeaseVaultDbContext : DbContext
{
    public LeaseVaultDbContext(DbContextOptions<LeaseVaultDbContext> options) : base(options)
    {
    }

    public DbSet<Property> Properties => Set<Property>();
    public DbSet<Unit> Units => Set<Unit>();
    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<Lease> Leases => Set<Lease>();
    public DbSet<Document> Documents => Set<Document>();
    public DbSet<DocumentVersion> DocumentVersions => Set<DocumentVersion>();
    public DbSet<DocumentTag> DocumentTags => Set<DocumentTag>();
    public DbSet<ApprovalWorkflow> ApprovalWorkflows => Set<ApprovalWorkflow>();
    public DbSet<ApprovalStep> ApprovalSteps => Set<ApprovalStep>();
    public DbSet<AuditEntry> AuditEntries => Set<AuditEntry>();
    public DbSet<RetentionPolicy> RetentionPolicies => Set<RetentionPolicy>();

    public bool IsSqlite => Database.ProviderName?.EndsWith("Sqlite", StringComparison.OrdinalIgnoreCase) == true;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(LeaseVaultDbContext).Assembly);
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        EnforceImmutability();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        EnforceImmutability();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    /// <summary>
    /// Audit entries and document versions are append-only. Any attempt to modify or delete
    /// them is rejected before reaching the database, regardless of which code path tried.
    /// </summary>
    private void EnforceImmutability()
    {
        foreach (var entry in ChangeTracker.Entries())
        {
            if (entry.State is not (EntityState.Modified or EntityState.Deleted))
            {
                continue;
            }

            if (entry.Entity is AuditEntry)
            {
                throw new InvalidOperationException("Audit entries are immutable and cannot be modified or deleted.");
            }

            if (entry.Entity is DocumentVersion && entry.State == EntityState.Modified)
            {
                throw new InvalidOperationException("Document versions are immutable; add a new version instead.");
            }
        }
    }
}
