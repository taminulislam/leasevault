using LeaseVault.Core.Abstractions;
using LeaseVault.Core.Domain;
using LeaseVault.Infrastructure.Persistence;
using LeaseVault.Infrastructure.Services;
using LeaseVault.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace LeaseVault.Tests.Infrastructure;

public class LeaseExpiryNotifierTests : IDisposable
{
    private readonly SqliteTestDatabase _database = new();
    private readonly FakeClock _clock = new(TestData.Now);
    private readonly RecordingEmailSender _email = new();

    public void Dispose() => _database.Dispose();

    private LeaseExpiryNotifier Build(LeaseVaultDbContext db, int leadDays = 60) =>
        new(db, _email, new EfAuditLog(db, new TestUser("system"), _clock), _clock,
            Options.Create(new ReminderOptions { LeadDays = leadDays, NotifyAddress = "leasing@x" }),
            TestData.Logger<LeaseExpiryNotifier>());

    [Fact]
    public async Task Sends_one_reminder_per_lease_inside_the_lead_window_and_expires_past_leases()
    {
        var today = DateOnly.FromDateTime(_clock.UtcNow);
        await using (var seed = _database.CreateContext())
        {
            var property = TestData.NewProperty();
            var tenant = TestData.NewTenant();
            seed.AddRange(property, tenant);
            await seed.SaveChangesAsync();

            var unit = property.Units.First();
            seed.Leases.AddRange(
                new Lease { Unit = unit, Tenant = tenant, StartDate = today.AddYears(-1), EndDate = today.AddDays(30), Status = LeaseStatus.Active, MonthlyRent = 1 },
                new Lease { Unit = unit, Tenant = tenant, StartDate = today.AddYears(-1), EndDate = today.AddDays(90), Status = LeaseStatus.Active, MonthlyRent = 1 },
                new Lease { Unit = unit, Tenant = tenant, StartDate = today.AddYears(-2), EndDate = today.AddDays(-1), Status = LeaseStatus.Active, MonthlyRent = 1 });
            await seed.SaveChangesAsync();
        }

        await using var db = _database.CreateContext();
        var notifier = Build(db);

        Assert.Equal(1, await notifier.SendDueRemindersAsync());
        Assert.Equal(0, await notifier.SendDueRemindersAsync()); // idempotent

        var mail = Assert.Single(_email.Sent);
        Assert.Equal("leases@lakeshore.example", mail.To);
        Assert.Equal("leasing@x", mail.Cc);
        Assert.Contains("30 day", mail.Subject);

        var leases = await db.Leases.OrderBy(l => l.EndDate).ToListAsync();
        Assert.Equal(LeaseStatus.Expired, leases[0].Status);
        Assert.Equal(LeaseStatus.Expiring, leases[1].Status);
        Assert.NotNull(leases[1].ExpiryReminderSentUtc);
        Assert.Equal(LeaseStatus.Active, leases[2].Status);
        Assert.Contains(await db.AuditEntries.ToListAsync(), a => a.Action == AuditActions.ReminderSent);
    }
}
