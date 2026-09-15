using LeaseVault.Core.Domain;
using LeaseVault.Core.Security;
using LeaseVault.Infrastructure.Persistence;
using LeaseVault.Infrastructure.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LeaseVault.Web.Pages.Documents;

[Authorize(Policy = Policies.Contributors)]
public class CreateModel : DocumentFormModel
{
    private readonly DocumentService _documents;

    public CreateModel(LeaseVaultDbContext db, DocumentService documents) : base(db)
    {
        _documents = documents;
    }

    [BindProperty]
    public DocumentInput Input { get; set; } = new();

    [BindProperty]
    public IFormFile? Upload { get; set; }

    [BindProperty]
    public string? Comment { get; set; }

    public async Task OnGetAsync(int? leaseId, int? propertyId, int? tenantId)
    {
        if (leaseId is int id)
        {
            var lease = await Db.Leases.Include(l => l.Unit).AsNoTracking().FirstOrDefaultAsync(l => l.Id == id);
            if (lease is not null)
            {
                Input.LeaseId = lease.Id;
                Input.PropertyId = lease.Unit?.PropertyId;
                Input.TenantId = lease.TenantId;
            }
        }

        Input.PropertyId ??= propertyId;
        Input.TenantId ??= tenantId;
        await LoadOptionsAsync();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid)
        {
            await LoadOptionsAsync();
            return Page();
        }

        var document = await _documents.CreateAsync(Input.ToRequest());

        if (Upload is { Length: > 0 })
        {
            try
            {
                await using var stream = Upload.OpenReadStream();
                await _documents.AddVersionAsync(document.Id, stream, Upload.FileName, Upload.ContentType, Comment ?? "Initial upload");
            }
            catch (DomainException ex)
            {
                TempData["Error"] = $"Document created but the file was rejected: {ex.Message}";
                return RedirectToPage("Details", new { id = document.Id });
            }
        }

        TempData["Success"] = $"Document '{document.Title}' created.";
        return RedirectToPage("Details", new { id = document.Id });
    }
}
