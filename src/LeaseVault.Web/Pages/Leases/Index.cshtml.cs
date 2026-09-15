using LeaseVault.Core.Abstractions;
using LeaseVault.Core.Domain;
using LeaseVault.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace LeaseVault.Web.Pages.Leases;

public class IndexModel : PageModel
{
    private readonly LeaseVaultDbContext _db;
    private readonly IClock _clock;

    public IndexModel(LeaseVaultDbContext db, IClock clock)
    {
        _db = db;
        _clock = clock;
    }

    public List<Lease> Leases { get; private set; } = [];
    public DateOnly Today => DateOnly.FromDateTime(_clock.UtcNow);

    public async Task OnGetAsync()
    {
        Leases = await _db.Leases
            .Include(l => l.Tenant)
            .Include(l => l.Unit).ThenInclude(u => u!.Property)
            .Include(l => l.Documents)
            .OrderBy(l => l.EndDate)
            .AsNoTracking()
            .ToListAsync();
    }
}
