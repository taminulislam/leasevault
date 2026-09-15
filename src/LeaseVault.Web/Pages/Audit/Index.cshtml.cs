using LeaseVault.Core.Domain;
using LeaseVault.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace LeaseVault.Web.Pages.Audit;

public class IndexModel : PageModel
{
    private readonly LeaseVaultDbContext _db;

    public IndexModel(LeaseVaultDbContext db)
    {
        _db = db;
    }

    public List<AuditEntry> Entries { get; private set; } = [];

    public async Task OnGetAsync()
    {
        Entries = await _db.AuditEntries.OrderByDescending(a => a.TimestampUtc).Take(500).AsNoTracking().ToListAsync();
    }
}
