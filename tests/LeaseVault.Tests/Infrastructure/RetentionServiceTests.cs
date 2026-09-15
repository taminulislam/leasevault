using System.Text;
using LeaseVault.Core.Abstractions;
using LeaseVault.Core.Domain;
using LeaseVault.Infrastructure.Persistence;
using LeaseVault.Infrastructure.Services;
using LeaseVault.Infrastructure.Storage;
using LeaseVault.Tests.Support;
using Microsoft.EntityFrameworkCore;

namespace LeaseVault.Tests.Infrastructure;

public class RetentionServiceTests : IDisposable
{
    private readonly SqliteTestDatabase _database = new();
    private readonly FakeClock _clock = new(TestData.Now);
    private readonly string _root = Path.Combine(Path.GetTempPath(), "leasevault-tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        _database.Dispose();
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Fact]
    public async Task Sweep_archives_or_deletes_documents_past_retention_and_leaves_others_alone()
    {
        var storage = new LocalDiskDocumentStorage(_root, TestData.Logger<LocalDiskDocumentStorage>());
        var today = DateOnly.FromDateTime(_clock.UtcNow);

        int archiveId, deleteId, keepId;
        string deletedKey;
        await using (var seed = _database.CreateContext())
        {
            var archivePolicy = new RetentionPolicy { Name = "lease 7y", Category = DocumentCategory.Lease, RetentionYears = 7, Action = DisposalAction.Archive };
            var deletePolicy = new RetentionPolicy { Name = "insurance 3y", Category = DocumentCategory.Insurance, RetentionYears = 3, Action = DisposalAction.Delete };
            seed.RetentionPolicies.AddRange(archivePolicy, deletePolicy);

            var oldLease = new Document { Title = "Old lease", Category = DocumentCategory.Lease, CreatedBy = "seed", CreatedUtc = _clock.UtcNow.AddYears(-8) };
            var oldCoi = new Document { Title = "Old COI", Category = DocumentCategory.Insurance, CreatedBy = "seed", CreatedUtc = _clock.UtcNow.AddYears(-4) };
            var recent = new Document { Title = "Recent lease", Category = DocumentCategory.Lease, CreatedBy = "seed", CreatedUtc = _clock.UtcNow.AddYears(-1) };
            seed.Documents.AddRange(oldLease, oldCoi, recent);
            await seed.SaveChangesAsync();

            deletedKey = StorageKeys.ForVersion(oldCoi.Id, 1, "coi.pdf");
            using var content = new MemoryStream(Encoding.UTF8.GetBytes("certificate"));
            var stored = await storage.UploadAsync(deletedKey, content, "application/pdf");
            oldCoi.AddVersion("seed", "coi.pdf", "application/pdf", stored.SizeBytes, deletedKey, stored.Sha256, _clock.UtcNow.AddYears(-4));
            await seed.SaveChangesAsync();

            (archiveId, deleteId, keepId) = (oldLease.Id, oldCoi.Id, recent.Id);
        }

        await using var db = _database.CreateContext();
        var service = new RetentionService(db, storage, new EfAuditLog(db, new TestUser("system"), _clock), _clock, TestData.Logger<RetentionService>());

        var result = await service.SweepAsync();

        Assert.Equal(new RetentionSweepResult(Archived: 1, Disposed: 1), result);
        Assert.Equal(DocumentStatus.Archived, (await db.Documents.FindAsync(archiveId))!.Status);
        Assert.Equal(DocumentStatus.Disposed, (await db.Documents.FindAsync(deleteId))!.Status);
        Assert.Equal(DocumentStatus.Draft, (await db.Documents.FindAsync(keepId))!.Status);
        Assert.False(await storage.ExistsAsync(deletedKey));
        Assert.Equal(2, await db.AuditEntries.CountAsync(a => a.Action == AuditActions.Archived || a.Action == AuditActions.Disposed));

        Assert.Equal(new RetentionSweepResult(0, 0), await service.SweepAsync()); // nothing left to do
        _ = today;
    }
}
