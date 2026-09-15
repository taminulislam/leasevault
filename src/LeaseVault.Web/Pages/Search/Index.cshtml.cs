using LeaseVault.Core.Abstractions;
using LeaseVault.Core.Domain;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace LeaseVault.Web.Pages.Search;

public class IndexModel : PageModel
{
    private readonly ISearchService _search;

    public IndexModel(ISearchService search)
    {
        _search = search;
    }

    public string Provider => _search.ProviderName;
    public IEnumerable<string> Statuses => Enum.GetNames<DocumentStatus>();
    public IEnumerable<string> Categories => Enum.GetNames<DocumentCategory>();

    public void OnGet()
    {
    }
}
