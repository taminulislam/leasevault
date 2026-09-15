using LeaseVault.Core.Abstractions;
using LeaseVault.Core.Domain;
using LeaseVault.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LeaseVault.Infrastructure.Search;

/// <summary>Bound from the "Search" section.</summary>
public sealed class SearchOptions
{
    public const string SectionName = "Search";

    /// <summary>Force the LIKE fallback even when FTS5 is available (diagnostics / tests).</summary>
    public bool DisableFts5 { get; set; }

    /// <summary>Maximum facet values returned per facet.</summary>
    public int MaxFacetValues { get; set; } = 20;
}

/// <summary>Row shape returned by the provider-specific full-text queries.</summary>
public sealed class RankedId
{
    public int Id { get; set; }
    public double Rank { get; set; }
}

/// <summary>
/// Shared faceting, filtering and paging. Providers only implement the full-text match that
/// turns free text into a set of ranked document ids.
/// </summary>
public abstract class SearchServiceBase : ISearchService
{
    protected LeaseVaultDbContext Db { get; }
    protected SearchOptions Options { get; }

    protected SearchServiceBase(LeaseVaultDbContext db, SearchOptions options)
    {
        Db = db;
        Options = options;
    }

    public abstract string ProviderName { get; }

    public abstract Task EnsureIndexAsync(CancellationToken ct = default);

    /// <summary>Returns ranked ids for the text, or an empty dictionary when nothing matches.</summary>
    protected abstract Task<IReadOnlyDictionary<int, double>> MatchAsync(string text, CancellationToken ct);

    public async Task<SearchResult> SearchAsync(SearchQuery query, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        IQueryable<Document> matched = Db.Documents.AsNoTracking()
            .Include(d => d.Property)
            .Include(d => d.Tenant)
            .Include(d => d.Tags);

        IReadOnlyDictionary<int, double>? ranks = null;
        if (query.HasText)
        {
            ranks = await MatchAsync(query.Text!.Trim(), ct);
            var ids = ranks.Keys.ToList();
            matched = matched.Where(d => ids.Contains(d.Id));
        }

        var facets = await BuildFacetsAsync(matched, ct);

        var filtered = ApplyFilters(matched, query);
        var total = await filtered.CountAsync(ct);

        List<Document> page;
        if (ranks is { Count: > 0 })
        {
            // Relevance order lives in memory; matched sets are bounded by the full-text hit list.
            var all = await filtered.ToListAsync(ct);
            page = all.OrderByDescending(d => ranks.GetValueOrDefault(d.Id))
                      .ThenByDescending(d => d.CreatedUtc)
                      .Skip(query.Skip).Take(query.Take).ToList();
        }
        else
        {
            page = await filtered.OrderByDescending(d => d.CreatedUtc).ThenBy(d => d.Id)
                .Skip(query.Skip).Take(query.Take).ToListAsync(ct);
        }

        var hits = page.Select(d => new SearchHit(
            d.Id, d.Title, d.Category, d.Status,
            d.Property?.Name, d.Tenant?.Name,
            d.TagValues, d.CurrentVersion, d.CreatedUtc,
            ranks?.GetValueOrDefault(d.Id) ?? 0)).ToList();

        return new SearchResult
        {
            Hits = hits,
            TotalMatches = total,
            TotalDocuments = await Db.Documents.CountAsync(ct),
            Facets = facets,
            Provider = ProviderName
        };
    }

    private static IQueryable<Document> ApplyFilters(IQueryable<Document> q, SearchQuery query)
    {
        if (query.PropertyId is int pid)
        {
            q = q.Where(d => d.PropertyId == pid);
        }

        if (query.TenantId is int tid)
        {
            q = q.Where(d => d.TenantId == tid);
        }

        if (!string.IsNullOrWhiteSpace(query.Tag))
        {
            var tag = query.Tag.Trim().ToLowerInvariant();
            q = q.Where(d => d.Tags.Any(t => t.Value == tag));
        }

        if (query.Status is DocumentStatus status)
        {
            q = q.Where(d => d.Status == status);
        }

        if (query.Category is DocumentCategory category)
        {
            q = q.Where(d => d.Category == category);
        }

        return q;
    }

    private async Task<SearchFacets> BuildFacetsAsync(IQueryable<Document> matched, CancellationToken ct)
    {
        var rows = await matched.Select(d => new
        {
            d.Id,
            d.PropertyId,
            PropertyName = d.Property != null ? d.Property.Name : null,
            d.TenantId,
            TenantName = d.Tenant != null ? d.Tenant.Name : null,
            d.Status,
            Tags = d.Tags.Select(t => t.Value).ToList()
        }).ToListAsync(ct);

        var max = Options.MaxFacetValues;

        return new SearchFacets
        {
            Properties = rows.Where(r => r.PropertyId != null)
                .GroupBy(r => (r.PropertyId!.Value, r.PropertyName!))
                .Select(g => new FacetValue(g.Key.Item1.ToString(), g.Key.Item2, g.Count()))
                .OrderByDescending(f => f.Count).ThenBy(f => f.Label).Take(max).ToList(),
            Tenants = rows.Where(r => r.TenantId != null)
                .GroupBy(r => (r.TenantId!.Value, r.TenantName!))
                .Select(g => new FacetValue(g.Key.Item1.ToString(), g.Key.Item2, g.Count()))
                .OrderByDescending(f => f.Count).ThenBy(f => f.Label).Take(max).ToList(),
            Tags = rows.SelectMany(r => r.Tags)
                .GroupBy(t => t)
                .Select(g => new FacetValue(g.Key, g.Key, g.Count()))
                .OrderByDescending(f => f.Count).ThenBy(f => f.Label).Take(max).ToList(),
            Statuses = rows.GroupBy(r => r.Status)
                .Select(g => new FacetValue(g.Key.ToString(), g.Key.ToString(), g.Count()))
                .OrderByDescending(f => f.Count).ThenBy(f => f.Label).ToList()
        };
    }

    /// <summary>Splits free text into terms, dropping punctuation that would break the query grammar.</summary>
    protected static IReadOnlyList<string> Tokenise(string text) =>
        text.Split([' ', ',', ';', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(t => new string(t.Where(c => char.IsLetterOrDigit(c) || c is '-' or '_').ToArray()))
            .Where(t => t.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
}
