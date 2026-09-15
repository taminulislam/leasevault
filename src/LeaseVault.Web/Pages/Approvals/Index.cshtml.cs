using LeaseVault.Core.Abstractions;
using LeaseVault.Core.Domain;
using LeaseVault.Core.Security;
using LeaseVault.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace LeaseVault.Web.Pages.Approvals;

[Authorize(Policy = Policies.Approvers)]
public class IndexModel : PageModel
{
    private readonly LeaseVaultDbContext _db;
    private readonly ICurrentUser _user;
    private readonly IClock _clock;

    public IndexModel(LeaseVaultDbContext db, ICurrentUser user, IClock clock)
    {
        _db = db;
        _user = user;
        _clock = clock;
    }

    public List<ApprovalWorkflow> InProgress { get; private set; } = [];
    public List<ApprovalWorkflow> Completed { get; private set; } = [];
    public DateTime Now => _clock.UtcNow;

    public bool IsMine(ApprovalStep? step) => step is not null && step.CanActBy(_user.Name);

    public async Task OnGetAsync()
    {
        InProgress = await _db.ApprovalWorkflows.Include(w => w.Document).Include(w => w.Steps)
            .Where(w => w.Status == WorkflowStatus.InProgress).OrderBy(w => w.StartedUtc).AsNoTracking().ToListAsync();
        Completed = await _db.ApprovalWorkflows.Include(w => w.Document).Include(w => w.Steps)
            .Where(w => w.Status != WorkflowStatus.InProgress).OrderByDescending(w => w.CompletedUtc).Take(50).AsNoTracking().ToListAsync();
    }
}
