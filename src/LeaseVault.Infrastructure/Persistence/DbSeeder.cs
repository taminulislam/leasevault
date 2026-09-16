using System.Text;
using LeaseVault.Core.Abstractions;
using LeaseVault.Core.Approvals;
using LeaseVault.Core.Domain;
using LeaseVault.Infrastructure.Approvals;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LeaseVault.Infrastructure.Persistence;

/// <summary>
/// Creates the schema and loads demo data so the application is usable immediately after
/// <c>dotnet run</c>. Idempotent: does nothing when properties already exist.
/// </summary>
public sealed class DbSeeder
{
    private readonly LeaseVaultDbContext _db;
    private readonly IDocumentStorage _storage;
    private readonly ISearchService _search;
    private readonly IClock _clock;
    private readonly ApprovalOptions _approvals;
    private readonly ILogger<DbSeeder> _logger;

    public DbSeeder(
        LeaseVaultDbContext db,
        IDocumentStorage storage,
        ISearchService search,
        IClock clock,
        IOptions<ApprovalOptions> approvals,
        ILogger<DbSeeder> logger)
    {
        _db = db;
        _storage = storage;
        _search = search;
        _clock = clock;
        _approvals = approvals.Value;
        _logger = logger;
    }

    public async Task SeedAsync(CancellationToken ct = default)
    {
        await _db.Database.EnsureCreatedAsync(ct);
        await _search.EnsureIndexAsync(ct);

        if (await _db.Properties.AnyAsync(ct))
        {
            return;
        }

        _logger.LogInformation("Seeding demo data");
        var now = _clock.UtcNow;
        var today = DateOnly.FromDateTime(now);

        // ---- Portfolio -----------------------------------------------------------------------
        var riverside = new Property { Name = "Riverside Plaza", AddressLine1 = "100 W Randolph St", City = "Chicago", State = "IL", PostalCode = "60601", PropertyType = "Office" };
        var capitol = new Property { Name = "Capitol Commons", AddressLine1 = "400 E Adams St", City = "Springfield", State = "IL", PostalCode = "62701", PropertyType = "Retail" };
        var prairie = new Property { Name = "Prairie Logistics Park", AddressLine1 = "2200 Industrial Dr", City = "Peoria", State = "IL", PostalCode = "61615", PropertyType = "Industrial" };

        riverside.Units.Add(new Unit { UnitNumber = "1200", Floor = 12, SquareFeet = 8500 });
        riverside.Units.Add(new Unit { UnitNumber = "1410", Floor = 14, SquareFeet = 3200 });
        riverside.Units.Add(new Unit { UnitNumber = "0150", Floor = 1, SquareFeet = 1800 });
        capitol.Units.Add(new Unit { UnitNumber = "A", Floor = 1, SquareFeet = 4200 });
        capitol.Units.Add(new Unit { UnitNumber = "B", Floor = 1, SquareFeet = 2600 });
        prairie.Units.Add(new Unit { UnitNumber = "W1", Floor = 1, SquareFeet = 60000 });
        prairie.Units.Add(new Unit { UnitNumber = "W2", Floor = 1, SquareFeet = 45000 });

        var tenants = new[]
        {
            new Tenant { Name = "Lakeshore Analytics LLC", ContactEmail = "leases@lakeshore-analytics.example", ContactPhone = "312-555-0142", Industry = "Technology" },
            new Tenant { Name = "Prairie State Dental", ContactEmail = "office@prairiedental.example", ContactPhone = "217-555-0177", Industry = "Healthcare" },
            new Tenant { Name = "Midwest Freight Co.", ContactEmail = "contracts@midwestfreight.example", ContactPhone = "309-555-0198", Industry = "Logistics" },
            new Tenant { Name = "Bean & Leaf Cafe", ContactEmail = "hello@beanandleaf.example", ContactPhone = "217-555-0123", Industry = "Hospitality" }
        };

        _db.Properties.AddRange(riverside, capitol, prairie);
        _db.Tenants.AddRange(tenants);
        await _db.SaveChangesAsync(ct);

        // ---- Leases --------------------------------------------------------------------------
        var leases = new[]
        {
            new Lease { Unit = riverside.Units.ElementAt(0), Tenant = tenants[0], StartDate = today.AddYears(-2), EndDate = today.AddDays(45), MonthlyRent = 21250m, SecurityDeposit = 42500m, Status = LeaseStatus.Active, Notes = "Renewal negotiation in progress." },
            new Lease { Unit = riverside.Units.ElementAt(1), Tenant = tenants[1], StartDate = today.AddMonths(-8), EndDate = today.AddYears(2), MonthlyRent = 7400m, SecurityDeposit = 14800m, Status = LeaseStatus.Active },
            new Lease { Unit = capitol.Units.ElementAt(0), Tenant = tenants[3], StartDate = today.AddYears(-1), EndDate = today.AddDays(20), MonthlyRent = 5600m, SecurityDeposit = 11200m, Status = LeaseStatus.Active },
            new Lease { Unit = prairie.Units.ElementAt(0), Tenant = tenants[2], StartDate = today.AddYears(-5), EndDate = today.AddYears(-3), MonthlyRent = 32000m, SecurityDeposit = 64000m, Status = LeaseStatus.Expired },
            new Lease { Unit = prairie.Units.ElementAt(1), Tenant = tenants[2], StartDate = today.AddMonths(1), EndDate = today.AddYears(5), MonthlyRent = 27500m, SecurityDeposit = 55000m, Status = LeaseStatus.Draft }
        };
        _db.Leases.AddRange(leases);

        // ---- Retention policies --------------------------------------------------------------
        var policies = new[]
        {
            new RetentionPolicy { Name = "Lease agreements - 7 years", Category = DocumentCategory.Lease, RetentionYears = 7, Action = DisposalAction.Archive, Description = "Retain executed leases seven years after lease end, then archive." },
            new RetentionPolicy { Name = "Insurance certificates - 3 years", Category = DocumentCategory.Insurance, RetentionYears = 3, Action = DisposalAction.Delete, Description = "Certificates are disposable three years after the lease ends." },
            new RetentionPolicy { Name = "General correspondence - 2 years", Category = null, RetentionYears = 2, Action = DisposalAction.Archive, Description = "Catch-all policy for uncategorised documents." },
            new RetentionPolicy { Name = "Litigation hold", Category = null, RetentionYears = 99, Action = DisposalAction.Archive, LegalHold = true, Description = "Assign to documents subject to legal hold; never disposed automatically." }
        };
        _db.RetentionPolicies.AddRange(policies);
        await _db.SaveChangesAsync(ct);

        // ---- Documents with content ----------------------------------------------------------
        var docs = new List<Document>();
        docs.Add(await AddDocumentAsync("Riverside Plaza Suite 1200 lease agreement", "Executed office lease for Lakeshore Analytics, 8,500 sq ft, 12th floor.", DocumentCategory.Lease, riverside, tenants[0], leases[0], policies[0], ["lease", "office", "executed"], "agent@leasevault.local", now.AddMonths(-25), 2, ct));
        docs.Add(await AddDocumentAsync("Riverside Plaza Suite 1200 renewal proposal", "Draft renewal terms: 5-year extension with 3% annual escalation.", DocumentCategory.Amendment, riverside, tenants[0], leases[0], policies[0], ["renewal", "proposal", "office"], "agent@leasevault.local", now.AddDays(-6), 1, ct));
        docs.Add(await AddDocumentAsync("Prairie State Dental certificate of insurance", "General liability certificate naming landlord as additional insured.", DocumentCategory.Insurance, riverside, tenants[1], leases[1], policies[1], ["insurance", "coi"], "legal@leasevault.local", now.AddMonths(-7), 1, ct));
        docs.Add(await AddDocumentAsync("Capitol Commons Unit A retail lease", "Retail lease for Bean & Leaf Cafe including percentage rent clause.", DocumentCategory.Lease, capitol, tenants[3], leases[2], policies[0], ["lease", "retail", "percentage-rent"], "agent@leasevault.local", now.AddMonths(-13), 1, ct));
        docs.Add(await AddDocumentAsync("Prairie Logistics W1 warehouse lease (expired)", "Historic warehouse lease for Midwest Freight; lease ended three years ago.", DocumentCategory.Lease, prairie, tenants[2], leases[3], policies[0], ["lease", "warehouse", "historic"], "owner@leasevault.local", now.AddYears(-5), 1, ct));
        docs.Add(await AddDocumentAsync("Prairie Logistics W1 insurance certificate 2019", "Expired certificate retained under the insurance policy; eligible for disposal.", DocumentCategory.Insurance, prairie, tenants[2], leases[3], policies[1], ["insurance", "historic"], "legal@leasevault.local", now.AddYears(-5), 1, ct));
        docs.Add(await AddDocumentAsync("Riverside Plaza annual fire inspection report", "Fire marshal inspection, no deficiencies noted.", DocumentCategory.Inspection, riverside, null, null, policies[2], ["inspection", "fire-safety"], "agent@leasevault.local", now.AddMonths(-3), 1, ct));
        docs.Add(await AddDocumentAsync("Prairie Logistics W2 lease draft", "Draft warehouse lease for Midwest Freight expansion into W2.", DocumentCategory.Lease, prairie, tenants[2], leases[4], policies[0], ["lease", "warehouse", "draft"], "agent@leasevault.local", now.AddDays(-2), 1, ct));

        // Statuses and locks that make the demo interesting.
        docs[0].Status = DocumentStatus.Approved;
        docs[3].Status = DocumentStatus.Approved;
        docs[4].Status = DocumentStatus.Approved;
        docs[7].CheckOut("agent@leasevault.local", now.AddHours(-3));

        await _db.SaveChangesAsync(ct);

        // ---- Workflows -----------------------------------------------------------------------
        var assignments = new ApprovalChainAssignments(_approvals.DefaultAgent, _approvals.DefaultLegal, _approvals.DefaultOwner);

        // In progress, waiting at the Legal step, and already overdue so escalation has work to do.
        var renewal = ApprovalWorkflow.Start(docs[1], "agent@leasevault.local", assignments, now.AddDays(-5), _approvals.StepSlaHours, _approvals.FallbackOwner);
        renewal.Approve(assignments.Agent, now.AddDays(-4), "Terms look good.");

        // Fresh workflow waiting on the Agent.
        ApprovalWorkflow.Start(docs[2], "legal@leasevault.local", assignments, now.AddHours(-2), _approvals.StepSlaHours, _approvals.FallbackOwner);

        // Persist the workflows first so the audit rows below can reference their real ids.
        await _db.SaveChangesAsync(ct);

        _db.AuditEntries.AddRange(
            AuditEntry.Create("system", "Seeded", "Database", null, "Demo data created", now),
            AuditEntry.Create("agent@leasevault.local", AuditActions.WorkflowStarted, nameof(Document), docs[1].Id, "Renewal proposal submitted for approval", now.AddDays(-5)),
            AuditEntry.Create("agent@leasevault.local", AuditActions.StepApproved, nameof(ApprovalWorkflow), renewal.Id, "Agent step approved", now.AddDays(-4)),
            AuditEntry.Create("agent@leasevault.local", AuditActions.CheckedOut, nameof(Document), docs[7].Id, null, now.AddHours(-3)));

        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("Seeded {Properties} properties, {Leases} leases, {Documents} documents", 3, leases.Length, docs.Count);
    }

    private async Task<Document> AddDocumentAsync(
        string title, string description, DocumentCategory category,
        Property property, Tenant? tenant, Lease? lease, RetentionPolicy policy,
        string[] tags, string createdBy, DateTime createdUtc, int versions, CancellationToken ct)
    {
        var document = new Document
        {
            Title = title,
            Description = description,
            Category = category,
            Property = property,
            Tenant = tenant,
            Lease = lease,
            RetentionPolicy = policy,
            CreatedBy = createdBy,
            CreatedUtc = createdUtc,
            Status = DocumentStatus.Draft
        };
        document.SetTags(tags);
        _db.Documents.Add(document);
        await _db.SaveChangesAsync(ct); // need the id for storage keys

        // Seeded documents carry the same audit provenance a real upload would produce.
        _db.AuditEntries.Add(AuditEntry.Create(createdBy, AuditActions.Created, nameof(Document), document.Id, $"Title='{title}'", createdUtc));

        for (var v = 1; v <= versions; v++)
        {
            var fileName = $"{Slug(title)}-v{v}.txt";
            var body = $"LEASEVAULT DEMO DOCUMENT\r\nTitle: {title}\r\nVersion: {v}\r\n\r\n{description}\r\n";
            var key = StorageKeys.ForVersion(document.Id, v, fileName);
            using var content = new MemoryStream(Encoding.UTF8.GetBytes(body));
            var stored = await _storage.UploadAsync(key, content, "text/plain", ct);
            document.AddVersion(createdBy, fileName, "text/plain", stored.SizeBytes, key, stored.Sha256, createdUtc.AddDays(v - 1), v == 1 ? "Initial upload" : "Revised terms");
            _db.AuditEntries.Add(AuditEntry.Create(createdBy, AuditActions.VersionAdded, nameof(Document), document.Id,
                $"v{v} {fileName} ({stored.SizeBytes} bytes)", createdUtc.AddDays(v - 1)));
        }

        return document;
    }

    private static string Slug(string value) =>
        new string(value.ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : '-').ToArray()).Trim('-');
}
