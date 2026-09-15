using LeaseVault.Core.Domain;
using LeaseVault.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace LeaseVault.Web.Pages.Documents;

public class IndexModel : PageModel
{
    private readonly LeaseVaultDbContext _db;

    public IndexModel(LeaseVaultDbContext db)
    {
        _db = db;
    }

    public List<Document> Documents { get; private set; } = [];

    public async Task OnGetAsync()
    {
        Documents = await _db.Documents
            .Include(d => d.Property)
            .Include(d => d.Tenant)
            .Include(d => d.Tags)
            .OrderByDescending(d => d.CreatedUtc)
            .AsNoTracking()
            .ToListAsync();
    }
}
