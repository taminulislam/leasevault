using System.ComponentModel.DataAnnotations;
using LeaseVault.Core.Domain;
using LeaseVault.Infrastructure.Persistence;
using LeaseVault.Infrastructure.Services;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace LeaseVault.Web.Pages.Documents;

/// <summary>Form fields shared by the create and edit pages.</summary>
public sealed class DocumentInput
{
    [Required, MaxLength(200)]
    public string Title { get; set; } = string.Empty;

    [MaxLength(2000)]
    public string? Description { get; set; }

    public DocumentCategory Category { get; set; } = DocumentCategory.Lease;

    public int? PropertyId { get; set; }
    public int? TenantId { get; set; }
    public int? LeaseId { get; set; }
    public int? RetentionPolicyId { get; set; }

    [MaxLength(500)]
    [Display(Name = "Tags (comma separated)")]
    public string? Tags { get; set; }

    public NewDocumentRequest ToRequest() => new(
        Title, Description, Category, PropertyId, TenantId, LeaseId, RetentionPolicyId,
        (Tags ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

    public static DocumentInput From(Document d) => new()
    {
        Title = d.Title,
        Description = d.Description,
        Category = d.Category,
        PropertyId = d.PropertyId,
        TenantId = d.TenantId,
        LeaseId = d.LeaseId,
        RetentionPolicyId = d.RetentionPolicyId,
        Tags = string.Join(", ", d.TagValues)
    };
}

public abstract class DocumentFormModel : PageModel
{
    protected readonly LeaseVaultDbContext Db;

    protected DocumentFormModel(LeaseVaultDbContext db)
    {
        Db = db;
    }

    public List<SelectListItem> PropertyOptions { get; private set; } = [];
    public List<SelectListItem> TenantOptions { get; private set; } = [];
    public List<SelectListItem> LeaseOptions { get; private set; } = [];
    public List<SelectListItem> PolicyOptions { get; private set; } = [];
    public IEnumerable<SelectListItem> CategoryOptions =>
        Enum.GetValues<DocumentCategory>().Select(c => new SelectListItem(c.ToString(), c.ToString()));

    protected async Task LoadOptionsAsync()
    {
        PropertyOptions = await Db.Properties.OrderBy(p => p.Name).Select(p => new SelectListItem(p.Name, p.Id.ToString())).ToListAsync();
        TenantOptions = await Db.Tenants.OrderBy(t => t.Name).Select(t => new SelectListItem(t.Name, t.Id.ToString())).ToListAsync();
        LeaseOptions = await Db.Leases.Include(l => l.Unit).ThenInclude(u => u!.Property).Include(l => l.Tenant)
            .OrderByDescending(l => l.EndDate)
            .Select(l => new SelectListItem($"{l.Unit!.Property!.Name} {l.Unit.UnitNumber} - {l.Tenant!.Name} ({l.StartDate.Year}-{l.EndDate.Year})", l.Id.ToString()))
            .ToListAsync();
        PolicyOptions = await Db.RetentionPolicies.OrderBy(p => p.Name).Select(p => new SelectListItem(p.Name, p.Id.ToString())).ToListAsync();
    }
}
