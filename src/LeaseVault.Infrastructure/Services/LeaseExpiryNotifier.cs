using LeaseVault.Core.Abstractions;
using LeaseVault.Core.Domain;
using LeaseVault.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LeaseVault.Infrastructure.Services;

/// <summary>Bound from the "Reminders" section.</summary>
public sealed class ReminderOptions
{
    public const string SectionName = "Reminders";

    /// <summary>Days before the lease end date at which a reminder is sent.</summary>
    public int LeadDays { get; set; } = 60;

    public int PollMinutes { get; set; } = 60;

    /// <summary>Internal distribution list copied on every reminder.</summary>
    public string NotifyAddress { get; set; } = "leasing@leasevault.local";
}

/// <summary>
/// Finds leases entering the expiry window and e-mails the tenant and leasing team once.
/// Also transitions leases that passed their end date to <see cref="LeaseStatus.Expired"/>.
/// </summary>
public sealed class LeaseExpiryNotifier
{
    private readonly LeaseVaultDbContext _db;
    private readonly IEmailSender _email;
    private readonly IAuditLog _audit;
    private readonly IClock _clock;
    private readonly ReminderOptions _options;
    private readonly ILogger<LeaseExpiryNotifier> _logger;

    public LeaseExpiryNotifier(
        LeaseVaultDbContext db,
        IEmailSender email,
        IAuditLog audit,
        IClock clock,
        IOptions<ReminderOptions> options,
        ILogger<LeaseExpiryNotifier> logger)
    {
        _db = db;
        _email = email;
        _audit = audit;
        _clock = clock;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<int> SendDueRemindersAsync(CancellationToken ct = default)
    {
        var now = _clock.UtcNow;
        var today = DateOnly.FromDateTime(now);
        var horizon = today.AddDays(_options.LeadDays);

        var leases = await _db.Leases
            .Include(l => l.Tenant)
            .Include(l => l.Unit).ThenInclude(u => u!.Property)
            .Where(l => (l.Status == LeaseStatus.Active || l.Status == LeaseStatus.Expiring) && l.EndDate <= horizon)
            .ToListAsync(ct);

        var sent = 0;
        foreach (var lease in leases)
        {
            if (lease.EndDate < today)
            {
                lease.Status = LeaseStatus.Expired;
                continue;
            }

            if (!lease.NeedsExpiryReminder(today, _options.LeadDays))
            {
                continue;
            }

            var days = lease.DaysUntilExpiry(today);
            var where = $"{lease.Unit?.Property?.Name} unit {lease.Unit?.UnitNumber}";
            await _email.SendAsync(new EmailMessage(
                lease.Tenant!.ContactEmail,
                $"Lease expiry reminder: {where} ends in {days} day(s)",
                $"Dear {lease.Tenant.Name}, your lease for {where} ends on {lease.EndDate:yyyy-MM-dd}. Please contact us to discuss renewal.")
            { Cc = _options.NotifyAddress }, ct);

            lease.MarkReminderSent(now);
            await _audit.AppendAsync(AuditActions.ReminderSent, nameof(Lease), lease.Id, $"Expiry in {days} day(s)", ct);
            sent++;
        }

        await _db.SaveChangesAsync(ct);
        if (sent > 0)
        {
            _logger.LogInformation("Sent {Count} lease expiry reminder(s)", sent);
        }

        return sent;
    }
}
