using LeaseVault.Core.Abstractions;
using LeaseVault.Core.Domain;

namespace LeaseVault.Web.Api;

/// <summary>
/// DataTables server-side processing endpoint over <see cref="ISearchService"/>. Accepts the
/// standard draw/start/length parameters plus facet filters and returns the DataTables JSON shape.
/// </summary>
public static class SearchEndpoints
{
    public static IEndpointRouteBuilder MapSearchEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/search", SearchAsync).RequireAuthorization().WithName("SearchDocuments").WithTags("Search");
        return app;
    }

    private static async Task<IResult> SearchAsync(
        HttpRequest request,
        ISearchService search,
        int draw = 1,
        int start = 0,
        int length = 25,
        string? q = null,
        string? propertyId = null,
        string? tenantId = null,
        string? tag = null,
        string? status = null,
        string? category = null,
        CancellationToken ct = default)
    {
        // DataTables sends its global search box as search[value]; accept either.
        q ??= request.Query["search[value]"].FirstOrDefault();

        var query = new SearchQuery
        {
            Text = q,
            // Facet ids arrive as strings because DataTables sends "" for a cleared facet,
            // which int? model binding rejects with a 400.
            PropertyId = ParseId(propertyId),
            TenantId = ParseId(tenantId),
            Tag = string.IsNullOrWhiteSpace(tag) ? null : tag,
            Status = Enum.TryParse<DocumentStatus>(status, true, out var s) ? s : null,
            Category = Enum.TryParse<DocumentCategory>(category, true, out var c) ? c : null,
            Skip = Math.Max(0, start),
            Take = length <= 0 ? 25 : Math.Min(length, 200)
        };

        var result = await search.SearchAsync(query, ct);

        // Raw BM25 / CONTAINSTABLE ranks are engine-specific and can be vanishingly small for
        // common terms, so expose relevance as a 0-100 score relative to the best hit on the page.
        var bestRank = result.Hits.Count == 0 ? 0 : result.Hits.Max(h => h.Rank);

        return Results.Ok(new
        {
            draw,
            recordsTotal = result.TotalDocuments,
            recordsFiltered = result.TotalMatches,
            provider = result.Provider,
            facets = result.Facets,
            data = result.Hits.Select(h => new
            {
                id = h.DocumentId,
                title = h.Title,
                category = h.Category.ToString(),
                status = h.Status.ToString(),
                property = h.PropertyName,
                tenant = h.TenantName,
                tags = h.Tags,
                version = h.CurrentVersion,
                createdUtc = h.CreatedUtc,
                rank = Math.Round(h.Rank, 6),
                relevance = bestRank > 0 ? (int)Math.Round(h.Rank / bestRank * 100) : 0
            })
        });
    }

    /// <summary>Parses a facet id, treating empty/blank (a cleared facet) as "no filter".</summary>
    private static int? ParseId(string? value) =>
        int.TryParse(value, out var id) ? id : null;
}
