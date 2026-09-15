using LeaseVault.Core.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LeaseVault.Infrastructure.Persistence.Configurations;

public sealed class DocumentConfiguration : IEntityTypeConfiguration<Document>
{
    public void Configure(EntityTypeBuilder<Document> b)
    {
        b.ToTable("Documents");
        b.Property(d => d.Category).HasConversion<string>().HasMaxLength(30);
        b.Property(d => d.Status).HasConversion<string>().HasMaxLength(30);
        b.HasIndex(d => d.Status);
        b.HasIndex(d => d.Title);
        b.Ignore(d => d.LatestVersion);
        b.Ignore(d => d.TagValues);
        b.Ignore(d => d.IsCheckedOut);
        b.Ignore(d => d.NextVersionNumber);

        b.HasMany(d => d.Versions).WithOne(v => v.Document).HasForeignKey(v => v.DocumentId).OnDelete(DeleteBehavior.Cascade);
        b.HasMany(d => d.Tags).WithOne(t => t.Document).HasForeignKey(t => t.DocumentId).OnDelete(DeleteBehavior.Cascade);
        b.HasMany(d => d.Workflows).WithOne(w => w.Document).HasForeignKey(w => w.DocumentId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne(d => d.RetentionPolicy).WithMany(p => p.Documents).HasForeignKey(d => d.RetentionPolicyId).OnDelete(DeleteBehavior.SetNull);
    }
}

public sealed class DocumentVersionConfiguration : IEntityTypeConfiguration<DocumentVersion>
{
    public void Configure(EntityTypeBuilder<DocumentVersion> b)
    {
        b.ToTable("DocumentVersions");
        b.HasIndex(v => new { v.DocumentId, v.VersionNumber }).IsUnique();
    }
}

public sealed class DocumentTagConfiguration : IEntityTypeConfiguration<DocumentTag>
{
    public void Configure(EntityTypeBuilder<DocumentTag> b)
    {
        b.ToTable("DocumentTags");
        b.HasIndex(t => t.Value);
        b.HasIndex(t => new { t.DocumentId, t.Value }).IsUnique();
    }
}
