using LeaseVault.Core.Domain;
using LeaseVault.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace LeaseVault.Web.Pages.Properties;

public class DetailsModel : PageModel
{
    private readonly LeaseVaultDbContext _db;

    public DetailsModel(LeaseVaultDbContext db)
    {
        _db = db;
    }

    public Property Property { get; private set; } = null!;
    public List<Lease> Leases { get; private set; } = [];
    public List<Document> Documents { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync(int id)
    {
        var property = await _db.Properties.Include(p => p.Units).AsNoTracking().FirstOrDefaultAsync(p => p.Id == id);
        if (property is null)
        {
            return NotFound();
        }

        Property = property;
        Leases = await _db.Leases.Include(l => l.Tenant).Include(l => l.Unit)
            .Where(l => l.Unit!.PropertyId == id).OrderByDescending(l => l.EndDate).AsNoTracking().ToListAsync();
        Documents = await _db.Documents.Include(d => d.Tenant)
            .Where(d => d.PropertyId == id).OrderByDescending(d => d.CreatedUtc).AsNoTracking().ToListAsync();
        return Page();
    }
}
