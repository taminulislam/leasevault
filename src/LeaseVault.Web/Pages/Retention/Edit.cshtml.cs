using LeaseVault.Core.Abstractions;
using LeaseVault.Core.Domain;
using LeaseVault.Core.Security;
using LeaseVault.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace LeaseVault.Web.Pages.Retention;

[Authorize(Policy = Policies.Admins)]
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
    public RetentionPolicy Policy { get; set; } = new();

    public bool IsNew => Policy.Id == 0;

    public IEnumerable<SelectListItem> CategoryOptions =>
        new[] { new SelectListItem("(any category)", "") }
            .Concat(Enum.GetValues<DocumentCategory>().Select(c => new SelectListItem(c.ToString(), c.ToString())));

    public IEnumerable<SelectListItem> ActionOptions =>
        Enum.GetValues<DisposalAction>().Select(a => new SelectListItem(a.ToString(), a.ToString()));

    public async Task<IActionResult> OnGetAsync(int? id)
    {
        if (id is null)
        {
            return Page();
        }

        var policy = await _db.RetentionPolicies.FindAsync(id.Value);
        if (policy is null)
        {
            return NotFound();
        }

        Policy = policy;
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid)
        {
            return Page();
        }

        if (Policy.Id == 0)
        {
            _db.RetentionPolicies.Add(Policy);
            await _db.SaveChangesAsync();
            await _audit.AppendAsync(AuditActions.Created, nameof(RetentionPolicy), Policy.Id, Policy.Name);
        }
        else
        {
            var existing = await _db.RetentionPolicies.FindAsync(Policy.Id);
            if (existing is null)
            {
                return NotFound();
            }

            existing.Name = Policy.Name;
            existing.Category = Policy.Category;
            existing.RetentionYears = Policy.RetentionYears;
            existing.Action = Policy.Action;
            existing.LegalHold = Policy.LegalHold;
            existing.Description = Policy.Description;
            await _audit.AppendAsync(AuditActions.Updated, nameof(RetentionPolicy), Policy.Id, Policy.Name);
        }

        await _db.SaveChangesAsync();
        TempData["Success"] = "Retention policy saved.";
        return RedirectToPage("Index");
    }
}
