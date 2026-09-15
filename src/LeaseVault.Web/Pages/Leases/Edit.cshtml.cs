using LeaseVault.Core.Abstractions;
using LeaseVault.Core.Domain;
using LeaseVault.Core.Security;
using LeaseVault.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LeaseVault.Web.Pages.Leases;

[Authorize(Policy = Policies.Contributors)]
public class EditModel : LeaseFormModel
{
    private readonly IAuditLog _audit;

    public EditModel(LeaseVaultDbContext db, IAuditLog audit) : base(db)
    {
        _audit = audit;
    }

    [BindProperty]
    public Lease Lease { get; set; } = new();

    public async Task<IActionResult> OnGetAsync(int id)
    {
        var lease = await Db.Leases.FindAsync(id);
        if (lease is null)
        {
            return NotFound();
        }

        Lease = lease;
        await LoadOptionsAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(int id)
    {
        var existing = await Db.Leases.FindAsync(id);
        if (existing is null)
        {
            return NotFound();
        }

        ValidateDates(Lease);
        if (!ModelState.IsValid)
        {
            await LoadOptionsAsync();
            return Page();
        }

        existing.UnitId = Lease.UnitId;
        existing.TenantId = Lease.TenantId;
        existing.StartDate = Lease.StartDate;
        existing.EndDate = Lease.EndDate;
        existing.MonthlyRent = Lease.MonthlyRent;
        existing.SecurityDeposit = Lease.SecurityDeposit;
        existing.Notes = Lease.Notes;

        await _audit.AppendAsync(AuditActions.Updated, nameof(Lease), id, "Terms updated");
        await Db.SaveChangesAsync();
        TempData["Success"] = "Lease updated.";
        return RedirectToPage("Details", new { id });
    }
}
