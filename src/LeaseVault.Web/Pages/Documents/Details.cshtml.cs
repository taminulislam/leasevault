using LeaseVault.Core.Abstractions;
using LeaseVault.Core.Domain;
using LeaseVault.Core.Retention;
using LeaseVault.Core.Security;
using LeaseVault.Infrastructure.Persistence;
using LeaseVault.Infrastructure.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace LeaseVault.Web.Pages.Documents;

public class DetailsModel : PageModel
{
    private readonly LeaseVaultDbContext _db;
    private readonly DocumentService _documents;
    private readonly ApprovalService _approvals;
    private readonly ICurrentUser _user;
    private readonly IDocumentStorage _storage;

    public DetailsModel(LeaseVaultDbContext db, DocumentService documents, ApprovalService approvals, ICurrentUser user, IDocumentStorage storage)
    {
        _db = db;
        _documents = documents;
        _approvals = approvals;
        _user = user;
        _storage = storage;
    }

    public Document Document { get; private set; } = null!;
    public ApprovalWorkflow? ActiveWorkflow { get; private set; }
    public List<ApprovalWorkflow> PastWorkflows { get; private set; } = [];
    public List<AuditEntry> Audit { get; private set; } = [];
    public IReadOnlyList<StoredObjectInfo> StoredObjects { get; private set; } = [];
    public RetentionPolicy? EffectivePolicy { get; private set; }
    public DateOnly? DisposalDate { get; private set; }
    public string StorageProvider => _storage.ProviderName;

    public bool IsContributor => _user.IsInGroup(GroupNames.Agents) || _user.IsInGroup(GroupNames.Admins);
    public bool IsAdmin => _user.IsInGroup(GroupNames.Admins);
    public bool IsMine => string.Equals(Document.CheckedOutBy, _user.Name, StringComparison.OrdinalIgnoreCase);
    public bool CanUpload => IsContributor && !Document.IsLockedFor(_user.Name) && Document.Status is not (DocumentStatus.Archived or DocumentStatus.Disposed);

    [BindProperty]
    public IFormFile? Upload { get; set; }

    [BindProperty]
    public string? Comment { get; set; }

    public async Task<IActionResult> OnGetAsync(int id)
    {
        var document = await _db.Documents
            .Include(d => d.Property).Include(d => d.Tenant).Include(d => d.Lease).Include(d => d.RetentionPolicy)
            .Include(d => d.Tags).Include(d => d.Versions)
            .Include(d => d.Workflows).ThenInclude(w => w.Steps)
            .AsNoTracking()
            .FirstOrDefaultAsync(d => d.Id == id);
        if (document is null)
        {
            return NotFound();
        }

        Document = document;
        ActiveWorkflow = document.Workflows.FirstOrDefault(w => w.Status == WorkflowStatus.InProgress);
        PastWorkflows = document.Workflows.Where(w => w.Status != WorkflowStatus.InProgress).OrderByDescending(w => w.StartedUtc).ToList();

        var policies = await _db.RetentionPolicies.AsNoTracking().ToListAsync();
        EffectivePolicy = RetentionCalculator.ResolvePolicy(document, policies);
        DisposalDate = EffectivePolicy is null ? null : RetentionCalculator.DisposalDate(document, EffectivePolicy);

        var key = id.ToString();
        Audit = await _db.AuditEntries.Where(a => a.EntityType == nameof(Document) && a.EntityId == key)
            .OrderByDescending(a => a.TimestampUtc).Take(30).AsNoTracking().ToListAsync();

        try
        {
            StoredObjects = await _documents.ListStoredVersionsAsync(id);
        }
        catch (Exception)
        {
            StoredObjects = [];
        }

        return Page();
    }

    public Task<IActionResult> OnPostCheckOutAsync(int id) => RunAsync(id, () => _documents.CheckOutAsync(id), "Document checked out to you.");

    public Task<IActionResult> OnPostCheckInAsync(int id) => RunAsync(id, () => _documents.CheckInAsync(id), "Document checked in.");

    public Task<IActionResult> OnPostForceCheckInAsync(int id) =>
        _user.IsInGroup(GroupNames.Admins)
            ? RunAsync(id, () => _documents.CheckInAsync(id, force: true), "Lock broken and document checked in.")
            : Task.FromResult<IActionResult>(Forbid());

    public Task<IActionResult> OnPostSubmitAsync(int id) => RunAsync(id, () => _approvals.StartAsync(id), "Submitted for approval (Agent -> Legal -> Owner).");

    public Task<IActionResult> OnPostArchiveAsync(int id) => RunAsync(id, () => _documents.ArchiveAsync(id), "Document archived.");

    public async Task<IActionResult> OnPostUploadAsync(int id)
    {
        if (Upload is null || Upload.Length == 0)
        {
            TempData["Error"] = "Choose a file to upload.";
            return RedirectToPage(new { id });
        }

        return await RunAsync(id, async () =>
        {
            await using var stream = Upload.OpenReadStream();
            var version = await _documents.AddVersionAsync(id, stream, Upload.FileName, Upload.ContentType, Comment);
            return version.VersionNumber;
        }, "New version uploaded.");
    }

    private async Task<IActionResult> RunAsync<T>(int id, Func<Task<T>> action, string success)
    {
        if (!(_user.IsInGroup(GroupNames.Agents) || _user.IsInGroup(GroupNames.Admins)))
        {
            return Forbid();
        }

        try
        {
            await action();
            TempData["Success"] = success;
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
        catch (DomainException ex)
        {
            TempData["Error"] = ex.Message;
        }

        return RedirectToPage(new { id });
    }

    private Task<IActionResult> RunAsync(int id, Func<Task> action, string success) =>
        RunAsync(id, async () => { await action(); return true; }, success);
}
