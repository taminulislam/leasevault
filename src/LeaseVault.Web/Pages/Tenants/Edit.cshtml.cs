using LeaseVault.Core.Abstractions;
using LeaseVault.Core.Domain;
using LeaseVault.Core.Security;
using LeaseVault.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace LeaseVault.Web.Pages.Tenants;

[Authorize(Policy = Policies.Contributors)]
public class EditModel : PageModel
{
    private readonly LeaseVaultDbContext _db;
    private readonly IAuditLog _audit;

    public EditModel(LeaseVaultDbContext db, IAuditLog audit)
    {
        _db = db;
        _audit = audit;
    }

    [BindProperty]
    public Tenant Tenant { get; set; } = new();

    public async Task<IActionResult> OnGetAsync(int id)
    {
        var tenant = await _db.Tenants.FindAsync(id);
        if (tenant is null)
        {
            return NotFound();
        }

        Tenant = tenant;
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(int id)
    {
        var existing = await _db.Tenants.FindAsync(id);
        if (existing is null)
        {
            return NotFound();
        }

        if (!ModelState.IsValid)
        {
            return Page();
        }

        existing.Name = Tenant.Name;
        existing.Industry = Tenant.Industry;
        existing.ContactEmail = Tenant.ContactEmail;
        existing.ContactPhone = Tenant.ContactPhone;

        await _audit.AppendAsync(AuditActions.Updated, nameof(Tenant), id, existing.Name);
        await _db.SaveChangesAsync();
        TempData["Success"] = "Tenant updated.";
        return RedirectToPage("Index");
    }
}
