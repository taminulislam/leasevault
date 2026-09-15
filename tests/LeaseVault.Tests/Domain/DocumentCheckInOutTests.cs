using LeaseVault.Core.Domain;
using LeaseVault.Tests.Support;

namespace LeaseVault.Tests.Domain;

public class DocumentCheckInOutTests
{
    private static readonly DateTime T0 = TestData.Now;

    [Fact]
    public void CheckOut_records_lock_owner_and_time()
    {
        var doc = TestData.NewDocument();

        doc.CheckOut("agent@x", T0);

        Assert.True(doc.IsCheckedOut);
        Assert.Equal("agent@x", doc.CheckedOutBy);
        Assert.Equal(T0, doc.CheckedOutUtc);
    }

    [Fact]
    public void CheckOut_by_another_user_is_rejected_with_lock_owner()
    {
        var doc = TestData.NewDocument();
        doc.CheckOut("agent@x", T0);

        var ex = Assert.Throws<DocumentLockedException>(() => doc.CheckOut("legal@x", T0.AddMinutes(5)));

        Assert.Equal("agent@x", ex.LockOwner);
        Assert.Equal("agent@x", doc.CheckedOutBy);
    }

    [Fact]
    public void CheckOut_by_same_user_is_idempotent_and_refreshes_timestamp()
    {
        var doc = TestData.NewDocument();
        doc.CheckOut("agent@x", T0);

        doc.CheckOut("AGENT@x", T0.AddHours(1));

        Assert.Equal(T0.AddHours(1), doc.CheckedOutUtc);
    }

    [Fact]
    public void CheckIn_by_owner_releases_lock()
    {
        var doc = TestData.NewDocument();
        doc.CheckOut("agent@x", T0);

        doc.CheckIn("agent@x");

        Assert.False(doc.IsCheckedOut);
        Assert.Null(doc.CheckedOutUtc);
    }

    [Fact]
    public void CheckIn_by_other_user_requires_force()
    {
        var doc = TestData.NewDocument();
        doc.CheckOut("agent@x", T0);

        Assert.Throws<DocumentLockedException>(() => doc.CheckIn("admin@x"));
        doc.CheckIn("admin@x", force: true);

        Assert.False(doc.IsCheckedOut);
    }

    [Fact]
    public void CheckIn_when_not_checked_out_fails()
    {
        var doc = TestData.NewDocument();
        Assert.Throws<DomainException>(() => doc.CheckIn("agent@x"));
    }

    [Theory]
    [InlineData(DocumentStatus.Archived)]
    [InlineData(DocumentStatus.Disposed)]
    public void Terminal_documents_cannot_be_checked_out(DocumentStatus status)
    {
        var doc = TestData.NewDocument(status: status);
        Assert.Throws<DomainException>(() => doc.CheckOut("agent@x", T0));
    }

    [Fact]
    public void Archive_requires_document_to_be_checked_in()
    {
        var doc = TestData.NewDocument();
        doc.CheckOut("agent@x", T0);

        Assert.Throws<DomainException>(() => doc.Archive());
        doc.CheckIn("agent@x");
        doc.Archive();

        Assert.Equal(DocumentStatus.Archived, doc.Status);
    }
}
