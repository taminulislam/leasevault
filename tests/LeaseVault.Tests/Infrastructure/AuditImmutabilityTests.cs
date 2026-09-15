using LeaseVault.Core.Domain;
using LeaseVault.Infrastructure.Persistence;
using LeaseVault.Tests.Support;
using Microsoft.EntityFrameworkCore;

namespace LeaseVault.Tests.Infrastructure;

public class AuditImmutabilityTests : IDisposable
{
    private readonly SqliteTestDatabase _database = new();

    public void Dispose() => _database.Dispose();

    [Fact]
    public async Task Audit_entries_can_be_appended_but_never_modified_or_deleted()
    {
        await using var db = _database.CreateContext();
        var entry = AuditEntry.Create("agent@x", "CheckedOut", "Document", 1, null, TestData.Now);
        db.AuditEntries.Add(entry);
        await db.SaveChangesAsync();

        entry.Details = "tampered";
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());

        db.Entry(entry).State = EntityState.Unchanged;
        db.AuditEntries.Remove(entry);
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());

        await using var verify = _database.CreateContext();
        var persisted = await verify.AuditEntries.SingleAsync();
        Assert.Null(persisted.Details);
    }

    [Fact]
    public async Task Document_versions_are_immutable_once_written()
    {
        await using var db = _database.CreateContext();
        var doc = TestData.NewDocument(id: 0, versions: 1);
        db.Documents.Add(doc);
        await db.SaveChangesAsync();

        doc.Versions.First().Sha256 = "rewritten";
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task EfAuditLog_records_current_user_and_clock()
    {
        await using var db = _database.CreateContext();
        var log = new EfAuditLog(db, new TestUser("legal@x", "Legal"), new FakeClock(TestData.Now));

        await log.AppendAsync("StepApproved", "ApprovalWorkflow", 5, "Legal step approved");
        await db.SaveChangesAsync();

        var entry = await db.AuditEntries.SingleAsync();
        Assert.Equal("legal@x", entry.Actor);
        Assert.Equal("5", entry.EntityId);
        Assert.Equal(TestData.Now, entry.TimestampUtc);
    }
}
