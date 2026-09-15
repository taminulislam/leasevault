using System.Security.Claims;
using System.Text.Encodings.Web;
using LeaseVault.Infrastructure.Persistence;
using LeaseVault.Web.Security;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LeaseVault.Tests.Integration;

/// <summary>
/// Boots the real host against an in-memory SQLite database and a temporary storage folder,
/// replacing cookie/Entra authentication with a test scheme that signs every request in as an
/// administrator (or the user named in the X-Test-User header).
/// </summary>
public sealed class LeaseVaultWebApplicationFactory : WebApplicationFactory<Program>
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly string _storageRoot = Path.Combine(Path.GetTempPath(), "leasevault-tests", Guid.NewGuid().ToString("N"));

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        _connection.Open();
        Directory.CreateDirectory(_storageRoot);

        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Default", "");
        builder.UseSetting("Storage:LocalPath", _storageRoot);
        builder.UseSetting("Retention:Enabled", "false");
        builder.UseSetting("Reminders:PollMinutes", "600");
        builder.UseSetting("Approvals:EscalationPollMinutes", "600");

        builder.ConfigureServices(services =>
        {
            var descriptors = services.Where(d => d.ServiceType == typeof(DbContextOptions<LeaseVaultDbContext>) || d.ServiceType == typeof(DbContextOptions)).ToList();
            foreach (var d in descriptors)
            {
                services.Remove(d);
            }

            services.AddDbContext<LeaseVaultDbContext>(o => o.UseSqlite(_connection));

            services.AddAuthentication(TestAuthHandler.SchemeName)
                .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestAuthHandler.SchemeName, _ => { });
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            _connection.Dispose();
            if (Directory.Exists(_storageRoot))
            {
                Directory.Delete(_storageRoot, recursive: true);
            }
        }
    }
}

public sealed class TestAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName = "Test";
    public const string UserHeader = "X-Test-User";
    public const string GroupsHeader = "X-Test-Groups";

    public TestAuthHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : base(options, logger, encoder)
    {
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var user = Request.Headers.TryGetValue(UserHeader, out var u) && !string.IsNullOrEmpty(u) ? u.ToString() : "admin@leasevault.local";
        var groups = Request.Headers.TryGetValue(GroupsHeader, out var g) && !string.IsNullOrEmpty(g)
            ? g.ToString().Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            : ["Admins", "Agents", "Legal", "Owners"];

        var claims = new List<Claim> { new(ClaimTypes.Name, user), new("preferred_username", user), new(AppClaims.DisplayName, user) };
        claims.AddRange(groups.Select(x => new Claim(AppClaims.Group, x)));
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, SchemeName));
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, SchemeName)));
    }
}
