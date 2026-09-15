using LeaseVault.Core.Domain;
using LeaseVault.Core.Security;
using LeaseVault.Infrastructure.Persistence;
using LeaseVault.Infrastructure.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace LeaseVault.Web.Pages.Retention;

[Authorize(Policy = Core.Security.Policies.Admins)]
public class IndexModel : PageModel
{
    private readonly LeaseVaultDbContext _db;
    private readonly RetentionService _retention;

    public IndexModel(LeaseVaultDbContext db, RetentionService retention)
    {
        _db = db;
        _retention = retention;
    }

    public List<RetentionPolicy> Policies { get; private set; } = [];

    public async Task OnGetAsync()
    {
        Policies = await _db.RetentionPolicies.Include(p => p.Documents).OrderBy(p => p.Name).AsNoTracking().ToListAsync();
    }

    public async Task<IActionResult> OnPostSweepAsync()
    {
        var result = await _retention.SweepAsync();
        TempData["Success"] = $"Retention sweep complete: {result.Archived} archived, {result.Disposed} disposed.";
        return RedirectToPage();
    }
}
