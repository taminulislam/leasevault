using LeaseVault.Core.Abstractions;
using LeaseVault.Core.Domain;
using LeaseVault.Core.Security;
using LeaseVault.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace LeaseVault.Web.Pages.Tenants;

[Authorize(Policy = Policies.Contributors)]
public class CreateModel : PageModel
{
    private readonly LeaseVaultDbContext _db;
    private readonly IAuditLog _audit;

    public CreateModel(LeaseVaultDbContext db, IAuditLog audit)
    {
        _db = db;
        _audit = audit;
    }

    [BindProperty]
    public Tenant Tenant { get; set; } = new();

    public void OnGet()
    {
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid)
        {
            return Page();
        }

        _db.Tenants.Add(Tenant);
        await _db.SaveChangesAsync();
        await _audit.AppendAsync(AuditActions.Created, nameof(Tenant), Tenant.Id, Tenant.Name);
        await _db.SaveChangesAsync();
        TempData["Success"] = $"Tenant '{Tenant.Name}' created.";
        return RedirectToPage("Index");
    }
}
