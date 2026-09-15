using LeaseVault.Core.Abstractions;
using LeaseVault.Core.Domain;
using LeaseVault.Core.Security;
using LeaseVault.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace LeaseVault.Web.Pages.Leases;

public class DetailsModel : PageModel
{
    private readonly LeaseVaultDbContext _db;
    private readonly IAuditLog _audit;
    private readonly ICurrentUser _user;
    private readonly IClock _clock;

    public DetailsModel(LeaseVaultDbContext db, IAuditLog audit, ICurrentUser user, IClock clock)
    {
        _db = db;
        _audit = audit;
        _user = user;
        _clock = clock;
    }

    public Lease Lease { get; private set; } = null!;
    public List<Document> Documents { get; private set; } = [];
    public List<AuditEntry> Audit { get; private set; } = [];
    public DateOnly Today => DateOnly.FromDateTime(_clock.UtcNow);
    public bool CanManage => _user.IsInGroup(GroupNames.Agents) || _user.IsInGroup(GroupNames.Admins);

    public async Task<IActionResult> OnGetAsync(int id)
    {
        var lease = await _db.Leases
            .Include(l => l.Tenant)
            .Include(l => l.Unit).ThenInclude(u => u!.Property)
            .AsNoTracking()
            .FirstOrDefaultAsync(l => l.Id == id);
        if (lease is null)
        {
            return NotFound();
        }

        Lease = lease;
        Documents = await _db.Documents.Where(d => d.LeaseId == id).OrderByDescending(d => d.CreatedUtc).AsNoTracking().ToListAsync();
        var key = id.ToString();
        Audit = await _db.AuditEntries.Where(a => a.EntityType == nameof(Lease) && a.EntityId == key).OrderByDescending(a => a.TimestampUtc).Take(20).ToListAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostActivateAsync(int id) => await TransitionAsync(id, l => l.Activate(), "activated");

    public async Task<IActionResult> OnPostTerminateAsync(int id) => await TransitionAsync(id, l => l.Terminate(), "terminated");

    private async Task<IActionResult> TransitionAsync(int id, Action<Lease> action, string verb)
    {
        if (!CanManage)
        {
            return Forbid();
        }

        var lease = await _db.Leases.FindAsync(id);
        if (lease is null)
        {
            return NotFound();
        }

        try
        {
            action(lease);
            await _audit.AppendAsync(AuditActions.Updated, nameof(Lease), id, $"Lease {verb}");
            await _db.SaveChangesAsync();
            TempData["Success"] = $"Lease {verb}.";
        }
        catch (DomainException ex)
        {
            TempData["Error"] = ex.Message;
        }

        return RedirectToPage(new { id });
    }
}
