using LeaseVault.Core.Domain;
using LeaseVault.Core.Retention;
using LeaseVault.Tests.Support;

namespace LeaseVault.Tests.Retention;

public class RetentionCalculatorTests
{
    private static readonly DateOnly Today = new(2026, 3, 1);

    private static RetentionPolicy SevenYearArchive(DocumentCategory? category = DocumentCategory.Lease) =>
        new() { Id = 1, Name = "7y", Category = category, RetentionYears = 7, Action = DisposalAction.Archive };

    [Fact]
    public void Trigger_date_is_lease_end_for_lease_bound_documents_else_creation_date()
    {
        var doc = TestData.NewDocument();
        doc.CreatedUtc = new DateTime(2020, 5, 5, 0, 0, 0, DateTimeKind.Utc);
        Assert.Equal(new DateOnly(2020, 5, 5), RetentionCalculator.TriggerDate(doc));

        doc.Lease = new Lease { StartDate = new DateOnly(2018, 1, 1), EndDate = new DateOnly(2021, 12, 31), Status = LeaseStatus.Expired };
        Assert.Equal(new DateOnly(2021, 12, 31), RetentionCalculator.TriggerDate(doc));
        Assert.Equal(new DateOnly(2028, 12, 31), RetentionCalculator.DisposalDate(doc, SevenYearArchive()));
    }

    [Fact]
    public void Document_is_due_once_retention_period_has_elapsed()
    {
        var doc = TestData.NewDocument();
        doc.Lease = new Lease { StartDate = new DateOnly(2015, 1, 1), EndDate = new DateOnly(2019, 2, 28), Status = LeaseStatus.Expired };

        Assert.True(RetentionCalculator.IsDue(doc, SevenYearArchive(), Today));           // 2026-02-28 <= 2026-03-01
        Assert.False(RetentionCalculator.IsDue(doc, SevenYearArchive(), new DateOnly(2026, 2, 27)));
    }

    [Fact]
    public void Legal_hold_blocks_disposal()
    {
        var doc = TestData.NewDocument();
        doc.CreatedUtc = new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var hold = new RetentionPolicy { Name = "hold", RetentionYears = 1, LegalHold = true };

        Assert.False(RetentionCalculator.IsDue(doc, hold, Today));
    }

    [Fact]
    public void Running_lease_or_checked_out_document_is_never_due()
    {
        var policy = new RetentionPolicy { Name = "1y", RetentionYears = 1 };
        var doc = TestData.NewDocument();
        doc.CreatedUtc = new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        doc.Lease = new Lease { StartDate = new DateOnly(2000, 1, 1), EndDate = new DateOnly(2001, 1, 1), Status = LeaseStatus.Active };
        Assert.False(RetentionCalculator.IsDue(doc, policy, Today));

        doc.Lease.Status = LeaseStatus.Expired;
        Assert.True(RetentionCalculator.IsDue(doc, policy, Today));

        doc.CheckOut("agent@x", TestData.Now);
        Assert.False(RetentionCalculator.IsDue(doc, policy, Today));
    }

    [Fact]
    public void Archived_document_is_not_due_again_under_archive_policy_but_is_under_delete_policy()
    {
        var doc = TestData.NewDocument(status: DocumentStatus.Archived);
        doc.CreatedUtc = new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        Assert.False(RetentionCalculator.IsDue(doc, new RetentionPolicy { RetentionYears = 1, Action = DisposalAction.Archive }, Today));
        Assert.True(RetentionCalculator.IsDue(doc, new RetentionPolicy { RetentionYears = 1, Action = DisposalAction.Delete }, Today));
    }

    [Fact]
    public void Policy_resolution_prefers_explicit_then_category_then_catch_all()
    {
        var policies = new[]
        {
            new RetentionPolicy { Id = 1, Name = "lease", Category = DocumentCategory.Lease },
            new RetentionPolicy { Id = 2, Name = "insurance", Category = DocumentCategory.Insurance },
            new RetentionPolicy { Id = 3, Name = "default", Category = null }
        };

        var doc = TestData.NewDocument();
        doc.Category = DocumentCategory.Insurance;
        Assert.Equal("insurance", RetentionCalculator.ResolvePolicy(doc, policies)!.Name);

        doc.RetentionPolicyId = 1;
        Assert.Equal("lease", RetentionCalculator.ResolvePolicy(doc, policies)!.Name);

        doc.RetentionPolicyId = null;
        doc.Category = DocumentCategory.Other;
        Assert.Equal("default", RetentionCalculator.ResolvePolicy(doc, policies)!.Name);

        Assert.Null(RetentionCalculator.ResolvePolicy(doc, policies.Take(2)));
    }
}
