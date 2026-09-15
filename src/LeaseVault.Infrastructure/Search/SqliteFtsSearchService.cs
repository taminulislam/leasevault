using LeaseVault.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace LeaseVault.Infrastructure.Search;

/// <summary>
/// SQLite implementation. Creates an FTS5 virtual table <c>DocumentSearch</c> kept in sync with
/// <c>Documents</c> by triggers, ranked with <c>bm25()</c>. If the SQLite build has no FTS5
/// module the service degrades to a <c>LIKE</c> scan so search keeps working everywhere.
/// </summary>
public sealed class SqliteFtsSearchService : SearchServiceBase
{
    private readonly ILogger<SqliteFtsSearchService> _logger;
    private static readonly object Gate = new();
    private static readonly Dictionary<string, bool> FtsAvailability = new(StringComparer.OrdinalIgnoreCase);

    public SqliteFtsSearchService(LeaseVaultDbContext db, SearchOptions options, ILogger<SqliteFtsSearchService> logger)
        : base(db, options)
    {
        _logger = logger;
    }

    public override string ProviderName => IsFtsEnabled ? "SqliteFts5" : "SqliteLike";

    /// <summary>True when the FTS5 virtual table exists and is usable for this connection.</summary>
    public bool IsFtsEnabled
    {
        get
        {
            if (Options.DisableFts5)
            {
                return false;
            }

            lock (Gate)
            {
                return FtsAvailability.TryGetValue(ConnectionKey, out var ok) && ok;
            }
        }
    }

    private string ConnectionKey => Db.Database.GetConnectionString() ?? Db.Database.GetDbConnection().ConnectionString;

    public override async Task EnsureIndexAsync(CancellationToken ct = default)
    {
        if (Options.DisableFts5)
        {
            _logger.LogInformation("FTS5 disabled by configuration; using LIKE fallback for search");
            return;
        }

        const string ddl = """
            CREATE VIRTUAL TABLE IF NOT EXISTS DocumentSearch
                USING fts5(DocumentId UNINDEXED, Title, Description, TagsText, tokenize = 'porter unicode61');

            CREATE TRIGGER IF NOT EXISTS trg_Documents_search_ai AFTER INSERT ON Documents BEGIN
                INSERT INTO DocumentSearch(DocumentId, Title, Description, TagsText)
                VALUES (new.Id, new.Title, new.Description, new.TagsText);
            END;

            CREATE TRIGGER IF NOT EXISTS trg_Documents_search_ad AFTER DELETE ON Documents BEGIN
                DELETE FROM DocumentSearch WHERE DocumentId = old.Id;
            END;

            CREATE TRIGGER IF NOT EXISTS trg_Documents_search_au AFTER UPDATE OF Title, Description, TagsText ON Documents BEGIN
                DELETE FROM DocumentSearch WHERE DocumentId = old.Id;
                INSERT INTO DocumentSearch(DocumentId, Title, Description, TagsText)
                VALUES (new.Id, new.Title, new.Description, new.TagsText);
            END;

            INSERT INTO DocumentSearch(DocumentId, Title, Description, TagsText)
                SELECT Id, Title, Description, TagsText FROM Documents
                WHERE Id NOT IN (SELECT DocumentId FROM DocumentSearch);
            """;

        bool available;
        try
        {
            await Db.Database.ExecuteSqlRawAsync(ddl, ct);
            available = true;
            _logger.LogInformation("SQLite FTS5 index DocumentSearch is ready");
        }
        catch (SqliteException ex)
        {
            available = false;
            _logger.LogWarning(ex, "SQLite FTS5 is unavailable; search will use LIKE fallback");
        }

        lock (Gate)
        {
            FtsAvailability[ConnectionKey] = available;
        }
    }

    protected override async Task<IReadOnlyDictionary<int, double>> MatchAsync(string text, CancellationToken ct)
    {
        var terms = Tokenise(text);
        if (terms.Count == 0)
        {
            return new Dictionary<int, double>();
        }

        if (IsFtsEnabled)
        {
            try
            {
                return await MatchWithFtsAsync(terms, ct);
            }
            catch (SqliteException ex)
            {
                _logger.LogWarning(ex, "FTS5 query failed; falling back to LIKE for this request");
            }
        }

        return await MatchWithLikeAsync(terms, ct);
    }

    private async Task<IReadOnlyDictionary<int, double>> MatchWithFtsAsync(IReadOnlyList<string> terms, CancellationToken ct)
    {
        // "lease"* AND "renew"* : quoted prefix terms, so user input can never alter the grammar.
        var match = string.Join(" AND ", terms.Select(t => $"\"{t.Replace("\"", "\"\"")}\"*"));

        var ranked = await Db.Database
            .SqlQuery<RankedId>($"SELECT DocumentId AS Id, -bm25(DocumentSearch) AS Rank FROM DocumentSearch WHERE DocumentSearch MATCH {match}")
            .ToListAsync(ct);

        return ranked.GroupBy(r => r.Id).ToDictionary(g => g.Key, g => g.Max(r => r.Rank));
    }

    private async Task<IReadOnlyDictionary<int, double>> MatchWithLikeAsync(IReadOnlyList<string> terms, CancellationToken ct)
    {
        var query = Db.Documents.AsNoTracking().Select(d => new { d.Id, d.Title, d.Description, d.TagsText });
        foreach (var term in terms)
        {
            var pattern = $"%{term}%";
            query = query.Where(d =>
                EF.Functions.Like(d.Title, pattern) ||
                (d.Description != null && EF.Functions.Like(d.Description, pattern)) ||
                EF.Functions.Like(d.TagsText, pattern));
        }

        var rows = await query.ToListAsync(ct);

        // Rank: number of terms found in the title counts double.
        return rows.ToDictionary(
            r => r.Id,
            r => terms.Sum(t => (r.Title.Contains(t, StringComparison.OrdinalIgnoreCase) ? 2.0 : 0)
                              + (r.TagsText.Contains(t, StringComparison.OrdinalIgnoreCase) ? 1.0 : 0)
                              + ((r.Description?.Contains(t, StringComparison.OrdinalIgnoreCase) ?? false) ? 0.5 : 0)));
    }
}
