using LeaseVault.Core.Abstractions;
using LeaseVault.Core.Domain;
using LeaseVault.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace LeaseVault.Tests.Support;

/// <summary>Deterministic clock for time-based rules (SLA escalation, reminders, retention).</summary>
public sealed class FakeClock : IClock
{
    public FakeClock(DateTime utcNow)
    {
        UtcNow = utcNow;
    }

    public DateTime UtcNow { get; set; }

    public void Advance(TimeSpan by) => UtcNow += by;
}

public sealed class TestUser : ICurrentUser
{
    public TestUser(string name, params string[] groups)
    {
        Name = name;
        Groups = groups;
    }

    public bool IsAuthenticated => true;
    public string Name { get; set; }
    public string DisplayName => Name;
    public IReadOnlyCollection<string> Groups { get; set; }
}

public sealed class RecordingEmailSender : IEmailSender
{
    public List<EmailMessage> Sent { get; } = [];

    public Task SendAsync(EmailMessage message, CancellationToken ct = default)
    {
        Sent.Add(message);
        return Task.CompletedTask;
    }
}

/// <summary>
/// Owns an open in-memory SQLite connection so the schema survives across DbContext instances
/// for the lifetime of a test.
/// </summary>
public sealed class SqliteTestDatabase : IDisposable
{
    private readonly SqliteConnection _connection;

    public SqliteTestDatabase()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        using var db = CreateContext();
        db.Database.EnsureCreated();
    }

    public LeaseVaultDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<LeaseVaultDbContext>().UseSqlite(_connection).Options);

    public void Dispose() => _connection.Dispose();
}

public static class TestData
{
    public static readonly DateTime Now = new(2026, 3, 1, 9, 0, 0, DateTimeKind.Utc);

    public static Document NewDocument(int id = 1, DocumentStatus status = DocumentStatus.Draft, int versions = 1)
    {
        var doc = new Document
        {
            Id = id,
            Title = "Suite 1200 lease",
            Category = DocumentCategory.Lease,
            Status = status,
            CreatedBy = "agent@leasevault.local",
            CreatedUtc = Now.AddDays(-10)
        };

        for (var v = 1; v <= versions; v++)
        {
            doc.Versions.Add(new DocumentVersion
            {
                DocumentId = id,
                VersionNumber = v,
                FileName = $"lease-v{v}.pdf",
                ContentType = "application/pdf",
                SizeBytes = 1000 + v,
                StorageKey = StorageKeys.ForVersion(id, v, "lease.pdf"),
                Sha256 = $"hash{v}",
                UploadedBy = "agent@leasevault.local",
                UploadedUtc = Now.AddDays(-10 + v)
            });
        }

        doc.CurrentVersion = versions;
        return doc;
    }

    public static Property NewProperty(string name = "Riverside Plaza")
    {
        var property = new Property { Name = name, AddressLine1 = "100 W Randolph St", City = "Chicago", State = "IL", PostalCode = "60601" };
        property.Units.Add(new Unit { UnitNumber = "1200", SquareFeet = 8500 });
        return property;
    }

    public static Tenant NewTenant(string name = "Lakeshore Analytics") =>
        new() { Name = name, ContactEmail = "leases@lakeshore.example" };

    public static Microsoft.Extensions.Logging.ILogger<T> Logger<T>() => NullLogger<T>.Instance;
}
