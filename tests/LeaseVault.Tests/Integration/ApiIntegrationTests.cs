using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace LeaseVault.Tests.Integration;

public class ApiIntegrationTests : IClassFixture<LeaseVaultWebApplicationFactory>
{
    private readonly LeaseVaultWebApplicationFactory _factory;

    public ApiIntegrationTests(LeaseVaultWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private HttpClient Client(string? user = null, string? groups = null)
    {
        var client = _factory.CreateClient(new() { AllowAutoRedirect = false });
        if (user is not null)
        {
            client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, user);
        }

        if (groups is not null)
        {
            client.DefaultRequestHeaders.Add(TestAuthHandler.GroupsHeader, groups);
        }

        return client;
    }

    [Fact]
    public async Task Health_endpoint_reports_healthy()
    {
        var response = await Client().GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Upload_version_then_download_round_trips_content_and_bumps_version()
    {
        var client = Client("agent@leasevault.local", "Agents");
        var payload = Encoding.UTF8.GetBytes("Executed lease amendment " + Guid.NewGuid());

        using var content = new ByteArrayContent(payload);
        content.Headers.ContentType = new("text/plain");
        var upload = await client.PostAsync("/api/documents/1/versions?fileName=amendment.txt&comment=api", content);

        Assert.Equal(HttpStatusCode.Created, upload.StatusCode);
        var body = await upload.Content.ReadFromJsonAsync<JsonElement>();
        var version = body.GetProperty("version").GetInt32();
        Assert.Equal(3, version); // seeded document 1 has two versions
        Assert.Equal(payload.Length, body.GetProperty("sizeBytes").GetInt64());

        var download = await client.GetAsync($"/api/documents/1/versions/{version}");
        Assert.Equal(HttpStatusCode.OK, download.StatusCode);
        Assert.Equal("amendment.txt", download.Content.Headers.ContentDisposition?.FileName?.Trim('"'));
        Assert.Equal(payload, await download.Content.ReadAsByteArrayAsync());

        var missing = await client.GetAsync("/api/documents/1/versions/999");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    [Fact]
    public async Task Checkout_lock_is_enforced_across_users_and_released_on_checkin()
    {
        var agent = Client("agent@leasevault.local", "Agents");
        var otherAgent = Client("agent2@leasevault.local", "Agents");

        var checkout = await agent.PostAsync("/api/documents/3/checkout", null);
        Assert.Equal(HttpStatusCode.OK, checkout.StatusCode);

        var conflict = await otherAgent.PostAsync("/api/documents/3/checkout", null);
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        var problem = await conflict.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("agent@leasevault.local", problem.GetProperty("lockOwner").GetString());

        using var blocked = new StringContent("new content", Encoding.UTF8, "text/plain");
        Assert.Equal(HttpStatusCode.Conflict, (await otherAgent.PostAsync("/api/documents/3/versions?fileName=x.txt", blocked)).StatusCode);

        var forbiddenForce = await otherAgent.PostAsync("/api/documents/3/checkin?force=true", null);
        Assert.Equal(HttpStatusCode.Forbidden, forbiddenForce.StatusCode);

        var checkin = await agent.PostAsync("/api/documents/3/checkin", null);
        Assert.Equal(HttpStatusCode.OK, checkin.StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await otherAgent.PostAsync("/api/documents/3/checkout", null)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Client("admin@leasevault.local", "Admins").PostAsync("/api/documents/3/checkin?force=true", null)).StatusCode);
    }

    [Fact]
    public async Task Legal_users_cannot_upload_but_can_download_and_search()
    {
        var legal = Client("legal@leasevault.local", "Legal");

        using var content = new StringContent("x", Encoding.UTF8, "text/plain");
        Assert.Equal(HttpStatusCode.Forbidden, (await legal.PostAsync("/api/documents/1/versions?fileName=x.txt", content)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await legal.GetAsync("/api/documents/1/versions/1")).StatusCode);

        var search = await legal.GetFromJsonAsync<JsonElement>("/api/search?q=insurance&draw=7&length=10");
        Assert.Equal(7, search.GetProperty("draw").GetInt32());
        Assert.Equal("SqliteFts5", search.GetProperty("provider").GetString());
        Assert.True(search.GetProperty("recordsFiltered").GetInt32() >= 2);
        Assert.Contains(search.GetProperty("data").EnumerateArray(), d => d.GetProperty("tags").EnumerateArray().Any(t => t.GetString() == "insurance"));
    }

    [Fact]
    public async Task Razor_pages_render_for_authenticated_user()
    {
        var client = Client();

        foreach (var path in new[] { "/", "/Documents", "/Documents/Details/1", "/Leases", "/Search", "/Approvals", "/Retention" })
        {
            var response = await client.GetAsync(path);
            Assert.True(response.StatusCode == HttpStatusCode.OK, $"{path} returned {response.StatusCode}");
        }

        var agentOnly = Client("agent@leasevault.local", "Agents");
        var retention = await agentOnly.GetAsync("/Retention");
        Assert.Equal(HttpStatusCode.Forbidden, retention.StatusCode);
    }
}
