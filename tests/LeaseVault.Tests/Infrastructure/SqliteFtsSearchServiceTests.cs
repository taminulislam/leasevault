using LeaseVault.Core.Abstractions;
using LeaseVault.Core.Domain;
using LeaseVault.Infrastructure.Persistence;
using LeaseVault.Infrastructure.Search;
using LeaseVault.Tests.Support;

namespace LeaseVault.Tests.Infrastructure;

public class SqliteFtsSearchServiceTests : IDisposable
{
    private readonly SqliteTestDatabase _database = new();

    public void Dispose() => _database.Dispose();

    private async Task<(LeaseVaultDbContext Db, SqliteFtsSearchService Search)> ArrangeAsync(bool disableFts)
    {
        var db = _database.CreateContext();
        var search = new SqliteFtsSearchService(db, new SearchOptions { DisableFts5 = disableFts }, TestData.Logger<SqliteFtsSearchService>());
        await search.EnsureIndexAsync();

        var riverside = TestData.NewProperty("Riverside Plaza");
        var capitol = TestData.NewProperty("Capitol Commons");
        var lakeshore = TestData.NewTenant("Lakeshore Analytics");
        var cafe = TestData.NewTenant("Bean & Leaf Cafe");
        db.AddRange(riverside, capitol, lakeshore, cafe);
        await db.SaveChangesAsync();

        db.Documents.AddRange(
            Doc("Riverside Plaza suite 1200 lease agreement", "Executed office lease.", riverside, lakeshore, DocumentStatus.Approved, "lease", "office"),
            Doc("Suite 1200 renewal proposal", "Renewal terms for the office lease extension.", riverside, lakeshore, DocumentStatus.PendingApproval, "renewal", "office"),
            Doc("Capitol Commons retail lease", "Retail lease with percentage rent.", capitol, cafe, DocumentStatus.Approved, "lease", "retail"),
            Doc("Fire inspection report", "Annual fire marshal inspection.", riverside, null, DocumentStatus.Draft, "inspection"));
        await db.SaveChangesAsync();

        return (db, search);
    }

    private static Document Doc(string title, string description, Property property, Tenant? tenant, DocumentStatus status, params string[] tags)
    {
        var doc = new Document { Title = title, Description = description, Property = property, Tenant = tenant, Status = status, CreatedBy = "seed", CreatedUtc = TestData.Now };
        doc.SetTags(tags);
        return doc;
    }

    [Fact]
    public async Task Fts5_index_is_created_and_returns_ranked_prefix_matches()
    {
        var (_, search) = await ArrangeAsync(disableFts: false);
        Assert.True(search.IsFtsEnabled);
        Assert.Equal("SqliteFts5", search.ProviderName);

        var result = await search.SearchAsync(new SearchQuery { Text = "renew" });

        var hit = Assert.Single(result.Hits);
        Assert.Equal("Suite 1200 renewal proposal", hit.Title);
        Assert.True(hit.Rank > 0);
        Assert.Equal(4, result.TotalDocuments);
    }

    [Fact]
    public async Task Fts5_triggers_keep_index_in_sync_on_update()
    {
        var (db, search) = await ArrangeAsync(disableFts: false);
        var report = db.Documents.Single(d => d.Title.StartsWith("Fire"));
        report.Title = "Elevator inspection report";
        report.Description = "Annual elevator safety inspection.";
        await db.SaveChangesAsync();

        Assert.Empty((await search.SearchAsync(new SearchQuery { Text = "fire" })).Hits);
        Assert.Single((await search.SearchAsync(new SearchQuery { Text = "elevator" })).Hits);
    }

    [Fact]
    public async Task Facets_reflect_the_matched_set_and_filters_narrow_results()
    {
        var (_, search) = await ArrangeAsync(disableFts: false);

        var all = await search.SearchAsync(new SearchQuery { Text = "lease" });
        Assert.Equal(3, all.TotalMatches);
        Assert.Contains(all.Facets.Properties, f => f.Label == "Riverside Plaza" && f.Count == 2);
        Assert.Contains(all.Facets.Tags, f => f.Key == "office" && f.Count == 2);
        Assert.Contains(all.Facets.Statuses, f => f.Key == nameof(DocumentStatus.Approved) && f.Count == 2);

        var retailOnly = await search.SearchAsync(new SearchQuery { Text = "lease", Tag = "retail" });
        Assert.Equal("Capitol Commons retail lease", Assert.Single(retailOnly.Hits).Title);

        var pending = await search.SearchAsync(new SearchQuery { Text = "lease", Status = DocumentStatus.PendingApproval });
        Assert.Equal("Suite 1200 renewal proposal", Assert.Single(pending.Hits).Title);
    }

    [Fact]
    public async Task Like_fallback_is_used_when_fts5_is_disabled()
    {
        var (_, search) = await ArrangeAsync(disableFts: true);
        Assert.False(search.IsFtsEnabled);
        Assert.Equal("SqliteLike", search.ProviderName);

        var result = await search.SearchAsync(new SearchQuery { Text = "percentage rent" });

        Assert.Equal("Capitol Commons retail lease", Assert.Single(result.Hits).Title);
        Assert.Equal("SqliteLike", result.Provider);
    }

    [Fact]
    public async Task Empty_text_browses_with_paging_and_facets()
    {
        var (_, search) = await ArrangeAsync(disableFts: false);

        var page = await search.SearchAsync(new SearchQuery { Text = "  ", Skip = 1, Take = 2 });

        Assert.Equal(4, page.TotalMatches);
        Assert.Equal(2, page.Hits.Count);
        Assert.Equal(2, page.Facets.Properties.Count);
    }
}
