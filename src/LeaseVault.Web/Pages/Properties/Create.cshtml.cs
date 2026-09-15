using LeaseVault.Core.Abstractions;
using LeaseVault.Core.Domain;
using LeaseVault.Core.Security;
using LeaseVault.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace LeaseVault.Web.Pages.Properties;

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
    public Property Property { get; set; } = new();

    [BindProperty]
    public string? UnitNumbers { get; set; }

    public void OnGet()
    {
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid)
        {
            return Page();
        }

        foreach (var number in (UnitNumbers ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Distinct())
        {
            Property.Units.Add(new Unit { UnitNumber = number });
        }

        _db.Properties.Add(Property);
        await _db.SaveChangesAsync();
        await _audit.AppendAsync(AuditActions.Created, nameof(Property), Property.Id, Property.Name);
        await _db.SaveChangesAsync();

        TempData["Success"] = $"Property '{Property.Name}' created.";
        return RedirectToPage("Details", new { id = Property.Id });
    }
}
