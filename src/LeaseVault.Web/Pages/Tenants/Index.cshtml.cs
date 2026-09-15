using LeaseVault.Core.Domain;
using LeaseVault.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace LeaseVault.Web.Pages.Tenants;

public class IndexModel : PageModel
{
    private readonly LeaseVaultDbContext _db;

    public IndexModel(LeaseVaultDbContext db)
    {
        _db = db;
    }

    public List<Tenant> Tenants { get; private set; } = [];

    public async Task OnGetAsync()
    {
        Tenants = await _db.Tenants.Include(t => t.Leases).Include(t => t.Documents).OrderBy(t => t.Name).AsNoTracking().ToListAsync();
    }
}
