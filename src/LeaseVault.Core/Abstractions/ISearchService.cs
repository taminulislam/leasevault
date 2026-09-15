using LeaseVault.Core.Domain;

namespace LeaseVault.Core.Abstractions;

/// <summary>
/// Full-text document search with faceted filters. Implementations: SQL Server full-text
/// (CONTAINS) and SQLite FTS5 (with a LIKE fallback when FTS5 is not compiled in).
/// </summary>
public interface ISearchService
{
    /// <summary>Provider name shown in the UI so operators can see which engine served the query.</summary>
    string ProviderName { get; }

    /// <summary>Creates the full-text catalog/index or virtual table if it does not exist yet.</summary>
    Task EnsureIndexAsync(CancellationToken ct = default);

    Task<SearchResult> SearchAsync(SearchQuery query, CancellationToken ct = default);
}

public sealed class SearchQuery
{
    public string? Text { get; init; }
    public int? PropertyId { get; init; }
    public int? TenantId { get; init; }
    public string? Tag { get; init; }
    public DocumentStatus? Status { get; init; }
    public DocumentCategory? Category { get; init; }
    public int Skip { get; init; }
    public int Take { get; init; } = 25;

    public bool HasText => !string.IsNullOrWhiteSpace(Text);
}

public sealed record SearchHit(
    int DocumentId,
    string Title,
    DocumentCategory Category,
    DocumentStatus Status,
    string? PropertyName,
    string? TenantName,
    IReadOnlyList<string> Tags,
    int CurrentVersion,
    DateTime CreatedUtc,
    double Rank);

public sealed record FacetValue(string Key, string Label, int Count);

public sealed class SearchFacets
{
    public IReadOnlyList<FacetValue> Properties { get; init; } = [];
    public IReadOnlyList<FacetValue> Tenants { get; init; } = [];
    public IReadOnlyList<FacetValue> Tags { get; init; } = [];
    public IReadOnlyList<FacetValue> Statuses { get; init; } = [];
}

public sealed class SearchResult
{
    public IReadOnlyList<SearchHit> Hits { get; init; } = [];

    /// <summary>Number of hits before paging (after facet filters).</summary>
    public int TotalMatches { get; init; }

    /// <summary>Total documents in the repository (DataTables' recordsTotal).</summary>
    public int TotalDocuments { get; init; }

    public SearchFacets Facets { get; init; } = new();

    public string Provider { get; init; } = string.Empty;
}
