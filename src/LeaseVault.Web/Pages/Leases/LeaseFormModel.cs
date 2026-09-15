using LeaseVault.Core.Domain;
using LeaseVault.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace LeaseVault.Web.Pages.Leases;

/// <summary>Shared select-list loading for the lease create/edit forms.</summary>
public abstract class LeaseFormModel : PageModel
{
    protected readonly LeaseVaultDbContext Db;

    protected LeaseFormModel(LeaseVaultDbContext db)
    {
        Db = db;
    }

    public List<SelectListItem> UnitOptions { get; private set; } = [];
    public List<SelectListItem> TenantOptions { get; private set; } = [];

    protected async Task LoadOptionsAsync()
    {
        UnitOptions = await Db.Units.Include(u => u.Property).OrderBy(u => u.Property!.Name).ThenBy(u => u.UnitNumber)
            .Select(u => new SelectListItem($"{u.Property!.Name} - unit {u.UnitNumber}", u.Id.ToString())).ToListAsync();
        TenantOptions = await Db.Tenants.OrderBy(t => t.Name)
            .Select(t => new SelectListItem(t.Name, t.Id.ToString())).ToListAsync();
    }

    protected void ValidateDates(Lease lease)
    {
        if (lease.EndDate <= lease.StartDate)
        {
            ModelState.AddModelError("Lease.EndDate", "End date must be after the start date.");
        }
    }
}
