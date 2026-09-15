using LeaseVault.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace LeaseVault.Infrastructure.Search;

/// <summary>
/// SQL Server / Azure SQL full-text search. Uses <c>CONTAINSTABLE</c> for ranked prefix matching
/// and falls back to <c>FREETEXTTABLE</c> (inflectional, natural-language) when the strict query
/// finds nothing. The catalog and index are created by <c>db/fulltext.sql</c>; see
/// <see cref="EnsureIndexAsync"/> which applies the same statements idempotently when the
/// connecting identity has permission.
/// </summary>
public sealed class SqlServerFullTextSearchService : SearchServiceBase
{
    private readonly ILogger<SqlServerFullTextSearchService> _logger;

    public SqlServerFullTextSearchService(LeaseVaultDbContext db, SearchOptions options, ILogger<SqlServerFullTextSearchService> logger)
        : base(db, options)
    {
        _logger = logger;
    }

    public override string ProviderName => "SqlServerFullText";

    public override async Task EnsureIndexAsync(CancellationToken ct = default)
    {
        const string sql = """
            IF (SELECT FULLTEXTSERVICEPROPERTY('IsFullTextInstalled')) = 1
            BEGIN
                IF NOT EXISTS (SELECT 1 FROM sys.fulltext_catalogs WHERE name = 'LeaseVaultCatalog')
                    CREATE FULLTEXT CATALOG LeaseVaultCatalog AS DEFAULT;

                IF NOT EXISTS (SELECT 1 FROM sys.fulltext_indexes WHERE object_id = OBJECT_ID('dbo.Documents'))
                    CREATE FULLTEXT INDEX ON dbo.Documents (Title LANGUAGE 1033, Description LANGUAGE 1033, TagsText LANGUAGE 1033)
                        KEY INDEX PK_Documents ON LeaseVaultCatalog WITH CHANGE_TRACKING AUTO;
            END
            """;

        try
        {
            await Db.Database.ExecuteSqlRawAsync(sql, ct);
        }
        catch (Exception ex)
        {
            // Typical on Azure SQL when the app identity lacks ALTER permission: DBAs run db/fulltext.sql instead.
            _logger.LogWarning(ex, "Could not create the full-text index automatically; run db/fulltext.sql");
        }
    }

    protected override async Task<IReadOnlyDictionary<int, double>> MatchAsync(string text, CancellationToken ct)
    {
        var terms = Tokenise(text);
        if (terms.Count == 0)
        {
            return new Dictionary<int, double>();
        }

        // "lease*" AND "renewal*" -> ranked prefix search across the indexed columns.
        var containsExpression = string.Join(" AND ", terms.Select(t => $"\"{t.Replace("\"", "")}*\""));

        var ranked = await Db.Database
            .SqlQuery<RankedId>($"SELECT [KEY] AS Id, CAST([RANK] AS float) AS Rank FROM CONTAINSTABLE(dbo.Documents, (Title, Description, TagsText), {containsExpression})")
            .ToListAsync(ct);

        if (ranked.Count == 0)
        {
            var freeText = string.Join(' ', terms);
            ranked = await Db.Database
                .SqlQuery<RankedId>($"SELECT [KEY] AS Id, CAST([RANK] AS float) AS Rank FROM FREETEXTTABLE(dbo.Documents, (Title, Description, TagsText), {freeText})")
                .ToListAsync(ct);
        }

        return ranked.ToDictionary(r => r.Id, r => r.Rank);
    }
}
