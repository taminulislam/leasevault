using LeaseVault.Core.Abstractions;
using LeaseVault.Infrastructure.DependencyInjection;
using LeaseVault.Infrastructure.Persistence;
using LeaseVault.Web.Api;
using LeaseVault.Web.Security;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// ---- Logging (Serilog) --------------------------------------------------------------------
builder.Host.UseSerilog((context, services, configuration) => configuration
    .ReadFrom.Configuration(context.Configuration)
    .ReadFrom.Services(services)
    .Enrich.FromLogContext()
    .Enrich.WithProperty("Application", "LeaseVault")
    .WriteTo.Console());
// Application Insights sink: add package Serilog.Sinks.ApplicationInsights and
//   .WriteTo.ApplicationInsights(services.GetRequiredService<TelemetryConfiguration>(), TelemetryConverter.Traces)
// with builder.Services.AddApplicationInsightsTelemetry() and APPLICATIONINSIGHTS_CONNECTION_STRING set
// as an App Service setting (see docs/DEPLOYMENT.md).

// ---- Services -------------------------------------------------------------------------------
builder.Services.AddLeaseVaultInfrastructure(builder.Configuration);
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUser, HttpContextCurrentUser>();
builder.Services.AddLeaseVaultAuthentication(builder.Configuration);

builder.Services.AddRazorPages(options =>
{
    options.Conventions.AuthorizeFolder("/");
    options.Conventions.AllowAnonymousToFolder("/Account");
    options.Conventions.AllowAnonymousToPage("/Error");
    options.Conventions.AuthorizeFolder("/Retention", LeaseVault.Core.Security.Policies.Admins);
});

builder.Services.AddHealthChecks()
    .AddDbContextCheck<LeaseVaultDbContext>("database");

var app = builder.Build();

// ---- Schema + demo data ---------------------------------------------------------------------
using (var scope = app.Services.CreateScope())
{
    await scope.ServiceProvider.GetRequiredService<DbSeeder>().SeedAsync();
}

// ---- Pipeline -------------------------------------------------------------------------------
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
    app.UseHttpsRedirection();
}

app.UseSerilogRequestLogging();
app.UseStaticFiles();
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();

app.MapRazorPages();
app.MapHealthChecks("/health");
app.MapDocumentEndpoints();
app.MapSearchEndpoints();

app.Run();

/// <summary>Exposed so integration tests can bootstrap the host with WebApplicationFactory.</summary>
public partial class Program
{
}
