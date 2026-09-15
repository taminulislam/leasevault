using LeaseVault.Core.Abstractions;
using LeaseVault.Core.Domain;
using LeaseVault.Infrastructure.Persistence;
using LeaseVault.Infrastructure.Services;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace LeaseVault.Web.Pages;

public class IndexModel : PageModel
{
    private readonly LeaseVaultDbContext _db;
    private readonly ICurrentUser _user;
    private readonly IClock _clock;
    private readonly ReminderOptions _reminders;

    public IndexModel(LeaseVaultDbContext db, ICurrentUser user, IClock clock, IOptions<ReminderOptions> reminders)
    {
        _db = db;
        _user = user;
        _clock = clock;
        _reminders = reminders.Value;
    }

    public int PropertyCount { get; private set; }
    public int ActiveLeaseCount { get; private set; }
    public int DocumentCount { get; private set; }
    public int PendingApprovalCount { get; private set; }
    public int CheckedOutCount { get; private set; }
    public int LeadDays => _reminders.LeadDays;

    public List<Lease> ExpiringLeases { get; private set; } = [];
    public List<ApprovalStep> MyQueue { get; private set; } = [];
    public List<Document> RecentDocuments { get; private set; } = [];
    public List<AuditEntry> RecentAudit { get; private set; } = [];

    public async Task OnGetAsync()
    {
        var today = DateOnly.FromDateTime(_clock.UtcNow);
        var horizon = today.AddDays(LeadDays);

        PropertyCount = await _db.Properties.CountAsync();
        ActiveLeaseCount = await _db.Leases.CountAsync(l => l.Status == LeaseStatus.Active || l.Status == LeaseStatus.Expiring);
        DocumentCount = await _db.Documents.CountAsync();
        PendingApprovalCount = await _db.ApprovalWorkflows.CountAsync(w => w.Status == WorkflowStatus.InProgress);
        CheckedOutCount = await _db.Documents.CountAsync(d => d.CheckedOutBy != null);

        ExpiringLeases = await _db.Leases
            .Include(l => l.Tenant).Include(l => l.Unit).ThenInclude(u => u!.Property)
            .Where(l => (l.Status == LeaseStatus.Active || l.Status == LeaseStatus.Expiring) && l.EndDate <= horizon && l.EndDate >= today)
            .OrderBy(l => l.EndDate)
            .Take(10)
            .ToListAsync();

        var me = _user.Name;
        MyQueue = await _db.ApprovalSteps
            .Include(s => s.Workflow).ThenInclude(w => w!.Document)
            .Where(s => s.Status == StepStatus.Pending && (s.Assignee == me || s.DelegatedTo == me || s.EscalatedTo == me))
            .OrderBy(s => s.DueUtc)
            .Take(10)
            .ToListAsync();

        RecentDocuments = await _db.Documents
            .Include(d => d.Property)
            .OrderByDescending(d => d.CreatedUtc)
            .Take(8)
            .ToListAsync();

        RecentAudit = await _db.AuditEntries
            .OrderByDescending(a => a.TimestampUtc)
            .Take(8)
            .ToListAsync();
    }
}
