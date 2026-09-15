using LeaseVault.Core.Abstractions;
using LeaseVault.Core.Domain;
using LeaseVault.Core.Security;
using LeaseVault.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace LeaseVault.Web.Pages.Properties;

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
    public Property Property { get; set; } = new();

    [BindProperty]
    public string? NewUnitNumber { get; set; }

    public async Task<IActionResult> OnGetAsync(int id)
    {
        var property = await _db.Properties.Include(p => p.Units).AsNoTracking().FirstOrDefaultAsync(p => p.Id == id);
        if (property is null)
        {
            return NotFound();
        }

        Property = property;
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(int id)
    {
        var existing = await _db.Properties.Include(p => p.Units).FirstOrDefaultAsync(p => p.Id == id);
        if (existing is null)
        {
            return NotFound();
        }

        if (!ModelState.IsValid)
        {
            Property.Units = existing.Units;
            return Page();
        }

        existing.Name = Property.Name;
        existing.PropertyType = Property.PropertyType;
        existing.AddressLine1 = Property.AddressLine1;
        existing.City = Property.City;
        existing.State = Property.State;
        existing.PostalCode = Property.PostalCode;

        if (!string.IsNullOrWhiteSpace(NewUnitNumber) && existing.Units.All(u => u.UnitNumber != NewUnitNumber.Trim()))
        {
            existing.Units.Add(new Unit { UnitNumber = NewUnitNumber.Trim() });
        }

        await _audit.AppendAsync(AuditActions.Updated, nameof(Property), id, existing.Name);
        await _db.SaveChangesAsync();
        TempData["Success"] = "Property updated.";
        return RedirectToPage("Details", new { id });
    }
}
