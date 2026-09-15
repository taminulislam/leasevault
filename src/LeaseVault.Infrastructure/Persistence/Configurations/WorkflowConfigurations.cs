using LeaseVault.Core.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LeaseVault.Infrastructure.Persistence.Configurations;

public sealed class ApprovalWorkflowConfiguration : IEntityTypeConfiguration<ApprovalWorkflow>
{
    public void Configure(EntityTypeBuilder<ApprovalWorkflow> b)
    {
        b.ToTable("ApprovalWorkflows");
        b.Property(w => w.Status).HasConversion<string>().HasMaxLength(20);
        b.HasIndex(w => new { w.DocumentId, w.Status });
        b.Ignore(w => w.OrderedSteps);
        b.Ignore(w => w.CurrentStep);
        b.Ignore(w => w.IsComplete);
        b.Ignore(w => w.StepSla);
        b.HasMany(w => w.Steps).WithOne(s => s.Workflow).HasForeignKey(s => s.WorkflowId).OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class ApprovalStepConfiguration : IEntityTypeConfiguration<ApprovalStep>
{
    public void Configure(EntityTypeBuilder<ApprovalStep> b)
    {
        b.ToTable("ApprovalSteps");
        b.Property(s => s.Role).HasConversion<string>().HasMaxLength(20);
        b.Property(s => s.Status).HasConversion<string>().HasMaxLength(20);
        b.HasIndex(s => new { s.WorkflowId, s.Order }).IsUnique();
        b.HasIndex(s => new { s.Status, s.DueUtc });
        b.Ignore(s => s.Actors);
    }
}

public sealed class AuditEntryConfiguration : IEntityTypeConfiguration<AuditEntry>
{
    public void Configure(EntityTypeBuilder<AuditEntry> b)
    {
        b.ToTable("AuditEntries");
        b.HasIndex(a => a.TimestampUtc);
        b.HasIndex(a => new { a.EntityType, a.EntityId });
    }
}

public sealed class RetentionPolicyConfiguration : IEntityTypeConfiguration<RetentionPolicy>
{
    public void Configure(EntityTypeBuilder<RetentionPolicy> b)
    {
        b.ToTable("RetentionPolicies");
        b.Property(p => p.Category).HasConversion<string>().HasMaxLength(30);
        b.Property(p => p.Action).HasConversion<string>().HasMaxLength(20);
    }
}
