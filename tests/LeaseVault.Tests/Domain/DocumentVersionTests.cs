using LeaseVault.Core.Abstractions;
using LeaseVault.Core.Domain;
using LeaseVault.Tests.Support;

namespace LeaseVault.Tests.Domain;

public class DocumentVersionTests
{
    private static readonly DateTime T0 = TestData.Now;

    private static DocumentVersion Add(Document doc, string by, string hash, long size = 2048) =>
        doc.AddVersion(by, "lease.pdf", "application/pdf", size, StorageKeys.ForVersion(doc.Id, doc.NextVersionNumber, "lease.pdf"), hash, T0);

    [Fact]
    public void AddVersion_increments_version_number_and_tracks_current()
    {
        var doc = TestData.NewDocument(versions: 2);

        var v3 = Add(doc, "agent@x", "hash3");

        Assert.Equal(3, v3.VersionNumber);
        Assert.Equal(3, doc.CurrentVersion);
        Assert.Same(v3, doc.LatestVersion);
        Assert.Equal("documents/00000001/v0003.pdf", v3.StorageKey);
    }

    [Fact]
    public void AddVersion_allowed_by_lock_owner_but_not_others()
    {
        var doc = TestData.NewDocument();
        doc.CheckOut("agent@x", T0);

        Assert.Throws<DocumentLockedException>(() => Add(doc, "legal@x", "hashX"));
        var v = Add(doc, "agent@x", "hashY");

        Assert.Equal(2, v.VersionNumber);
    }

    [Fact]
    public void AddVersion_rejects_identical_content_and_empty_files()
    {
        var doc = TestData.NewDocument(versions: 1); // latest hash = hash1

        Assert.Throws<DomainException>(() => Add(doc, "agent@x", "HASH1"));
        Assert.Throws<DomainException>(() => Add(doc, "agent@x", "hash2", size: 0));
        Assert.Equal(1, doc.CurrentVersion);
    }

    [Fact]
    public void New_version_after_approval_returns_document_to_draft()
    {
        var doc = TestData.NewDocument(status: DocumentStatus.Approved);

        Add(doc, "agent@x", "hash2");

        Assert.Equal(DocumentStatus.Draft, doc.Status);
    }

    [Theory]
    [InlineData(DocumentStatus.Archived)]
    [InlineData(DocumentStatus.Disposed)]
    public void Versions_cannot_be_added_to_terminal_documents(DocumentStatus status)
    {
        var doc = TestData.NewDocument(status: status);
        Assert.Throws<DomainException>(() => Add(doc, "agent@x", "hash2"));
    }

    [Fact]
    public void SetTags_normalises_dedupes_and_maintains_search_text()
    {
        var doc = TestData.NewDocument();

        doc.SetTags([" Lease ", "office", "lease", "", "Executed"]);

        Assert.Equal(["executed", "lease", "office"], doc.TagValues);
        Assert.Equal("executed lease office", doc.TagsText);
    }
}
