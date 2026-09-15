using LeaseVault.Core.Domain;
using LeaseVault.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace LeaseVault.Web.Pages.Properties;

public class IndexModel : PageModel
{
    private readonly LeaseVaultDbContext _db;

    public IndexModel(LeaseVaultDbContext db)
    {
        _db = db;
    }

    public List<Property> Properties { get; private set; } = [];

    public async Task OnGetAsync()
    {
        Properties = await _db.Properties.Include(p => p.Units).Include(p => p.Documents).OrderBy(p => p.Name).ToListAsync();
    }
}
