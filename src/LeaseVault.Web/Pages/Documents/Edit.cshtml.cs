using LeaseVault.Core.Domain;
using LeaseVault.Core.Security;
using LeaseVault.Infrastructure.Persistence;
using LeaseVault.Infrastructure.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LeaseVault.Web.Pages.Documents;

[Authorize(Policy = Policies.Contributors)]
public class EditModel : DocumentFormModel
{
    private readonly DocumentService _documents;

    public EditModel(LeaseVaultDbContext db, DocumentService documents) : base(db)
    {
        _documents = documents;
    }

    [BindProperty]
    public DocumentInput Input { get; set; } = new();

    public int Id { get; private set; }

    public async Task<IActionResult> OnGetAsync(int id)
    {
        var document = await Db.Documents.Include(d => d.Tags).AsNoTracking().FirstOrDefaultAsync(d => d.Id == id);
        if (document is null)
        {
            return NotFound();
        }

        Id = id;
        Input = DocumentInput.From(document);
        await LoadOptionsAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(int id)
    {
        Id = id;
        if (!ModelState.IsValid)
        {
            await LoadOptionsAsync();
            return Page();
        }

        try
        {
            await _documents.UpdateMetadataAsync(id, Input.ToRequest());
            TempData["Success"] = "Document metadata updated.";
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
        catch (DomainException ex)
        {
            TempData["Error"] = ex.Message;
        }

        return RedirectToPage("Details", new { id });
    }
}
