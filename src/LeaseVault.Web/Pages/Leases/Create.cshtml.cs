using LeaseVault.Core.Abstractions;
using LeaseVault.Core.Domain;
using LeaseVault.Core.Security;
using LeaseVault.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LeaseVault.Web.Pages.Leases;

[Authorize(Policy = Policies.Contributors)]
public class CreateModel : LeaseFormModel
{
    private readonly IAuditLog _audit;
    private readonly IClock _clock;

    public CreateModel(LeaseVaultDbContext db, IAuditLog audit, IClock clock) : base(db)
    {
        _audit = audit;
        _clock = clock;
    }

    [BindProperty]
    public Lease Lease { get; set; } = new();

    public async Task OnGetAsync()
    {
        var today = DateOnly.FromDateTime(_clock.UtcNow);
        Lease.StartDate = today;
        Lease.EndDate = today.AddYears(1);
        await LoadOptionsAsync();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        ValidateDates(Lease);
        if (!ModelState.IsValid)
        {
            await LoadOptionsAsync();
            return Page();
        }

        Lease.Status = LeaseStatus.Draft;
        Db.Leases.Add(Lease);
        await Db.SaveChangesAsync();
        await _audit.AppendAsync(AuditActions.Created, nameof(Lease), Lease.Id, $"Unit {Lease.UnitId}, tenant {Lease.TenantId}");
        await Db.SaveChangesAsync();
        TempData["Success"] = "Lease created as draft. Activate it once the agreement is executed.";
        return RedirectToPage("Details", new { id = Lease.Id });
    }
}
