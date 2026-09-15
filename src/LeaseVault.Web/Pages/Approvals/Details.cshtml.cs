using LeaseVault.Core.Abstractions;
using LeaseVault.Core.Domain;
using LeaseVault.Core.Security;
using LeaseVault.Infrastructure.Persistence;
using LeaseVault.Infrastructure.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace LeaseVault.Web.Pages.Approvals;

[Authorize(Policy = Policies.Approvers)]
public class DetailsModel : PageModel
{
    private readonly LeaseVaultDbContext _db;
    private readonly ApprovalService _approvals;
    private readonly ICurrentUser _user;
    private readonly IClock _clock;

    public DetailsModel(LeaseVaultDbContext db, ApprovalService approvals, ICurrentUser user, IClock clock)
    {
        _db = db;
        _approvals = approvals;
        _user = user;
        _clock = clock;
    }

    public ApprovalWorkflow Workflow { get; private set; } = null!;
    public List<AuditEntry> Audit { get; private set; } = [];
    public bool CanAct => Workflow.CurrentStep?.CanActBy(_user.Name) == true;
    public bool CanCancel => !Workflow.IsComplete && (_user.IsInGroup(GroupNames.Admins) || string.Equals(Workflow.StartedBy, _user.Name, StringComparison.OrdinalIgnoreCase));
    public DateTime Now => _clock.UtcNow;

    [BindProperty]
    public string? Comment { get; set; }

    [BindProperty]
    public string? DelegateTo { get; set; }

    public async Task<IActionResult> OnGetAsync(int id)
    {
        var workflow = await _db.ApprovalWorkflows.Include(w => w.Document).Include(w => w.Steps).AsNoTracking().FirstOrDefaultAsync(w => w.Id == id);
        if (workflow is null)
        {
            return NotFound();
        }

        Workflow = workflow;
        var key = id.ToString();
        Audit = await _db.AuditEntries.Where(a => a.EntityType == nameof(ApprovalWorkflow) && a.EntityId == key).OrderByDescending(a => a.TimestampUtc).AsNoTracking().ToListAsync();
        return Page();
    }

    public Task<IActionResult> OnPostApproveAsync(int id) => RunAsync(id, () => _approvals.ApproveAsync(id, Comment), "Step approved.");

    public Task<IActionResult> OnPostRejectAsync(int id) => RunAsync(id, () => _approvals.RejectAsync(id, Comment ?? string.Empty), "Document rejected.");

    public Task<IActionResult> OnPostDelegateAsync(int id) => RunAsync(id, () => _approvals.DelegateAsync(id, DelegateTo ?? string.Empty), $"Step delegated to {DelegateTo}.");

    public Task<IActionResult> OnPostCancelAsync(int id) => RunAsync(id, async () => { await _approvals.CancelAsync(id); return true; }, "Workflow cancelled.");

    private async Task<IActionResult> RunAsync<T>(int id, Func<Task<T>> action, string success)
    {
        try
        {
            await action();
            TempData["Success"] = success;
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
        catch (ArgumentException ex)
        {
            TempData["Error"] = ex.Message;
        }
        catch (DomainException ex)
        {
            TempData["Error"] = ex.Message;
        }

        return RedirectToPage(new { id });
    }
}
