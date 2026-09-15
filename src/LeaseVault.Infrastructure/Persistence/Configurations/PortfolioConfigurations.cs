using LeaseVault.Core.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LeaseVault.Infrastructure.Persistence.Configurations;

public sealed class PropertyConfiguration : IEntityTypeConfiguration<Property>
{
    public void Configure(EntityTypeBuilder<Property> b)
    {
        b.ToTable("Properties");
        b.HasIndex(p => p.Name);
        b.HasMany(p => p.Units).WithOne(u => u.Property).HasForeignKey(u => u.PropertyId).OnDelete(DeleteBehavior.Cascade);
        b.HasMany(p => p.Documents).WithOne(d => d.Property).HasForeignKey(d => d.PropertyId).OnDelete(DeleteBehavior.SetNull);
    }
}

public sealed class UnitConfiguration : IEntityTypeConfiguration<Unit>
{
    public void Configure(EntityTypeBuilder<Unit> b)
    {
        b.ToTable("Units");
        b.HasIndex(u => new { u.PropertyId, u.UnitNumber }).IsUnique();
    }
}

public sealed class TenantConfiguration : IEntityTypeConfiguration<Tenant>
{
    public void Configure(EntityTypeBuilder<Tenant> b)
    {
        b.ToTable("Tenants");
        b.HasIndex(t => t.Name);
        b.HasMany(t => t.Documents).WithOne(d => d.Tenant).HasForeignKey(d => d.TenantId).OnDelete(DeleteBehavior.SetNull);
    }
}

public sealed class LeaseConfiguration : IEntityTypeConfiguration<Lease>
{
    public void Configure(EntityTypeBuilder<Lease> b)
    {
        b.ToTable("Leases");
        b.Property(l => l.MonthlyRent).HasPrecision(18, 2);
        b.Property(l => l.SecurityDeposit).HasPrecision(18, 2);
        b.Property(l => l.Status).HasConversion<string>().HasMaxLength(20);
        b.HasIndex(l => l.EndDate);
        b.HasOne(l => l.Unit).WithMany(u => u.Leases).HasForeignKey(l => l.UnitId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(l => l.Tenant).WithMany(t => t.Leases).HasForeignKey(l => l.TenantId).OnDelete(DeleteBehavior.Restrict);
        b.HasMany(l => l.Documents).WithOne(d => d.Lease).HasForeignKey(d => d.LeaseId).OnDelete(DeleteBehavior.SetNull);
    }
}
