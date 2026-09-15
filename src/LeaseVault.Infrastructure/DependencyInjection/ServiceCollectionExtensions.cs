using LeaseVault.Core.Abstractions;
using LeaseVault.Infrastructure.Approvals;
using LeaseVault.Infrastructure.BackgroundServices;
using LeaseVault.Infrastructure.Notifications;
using LeaseVault.Infrastructure.Persistence;
using LeaseVault.Infrastructure.Search;
using LeaseVault.Infrastructure.Services;
using LeaseVault.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LeaseVault.Infrastructure.DependencyInjection;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers persistence, storage, search, notifications, application services and the
    /// hosted background services. Provider selection is configuration-driven:
    /// <list type="bullet">
    /// <item><c>ConnectionStrings:Default</c> empty -> SQLite (app.db) + FTS5 search; set -> SQL Server + full-text search.</item>
    /// <item><c>Storage:AccountUri</c>/<c>ConnectionString</c> set -> Azure Blob; otherwise local disk.</item>
    /// </list>
    /// </summary>
    public static IServiceCollection AddLeaseVaultInfrastructure(this IServiceCollection services, IConfiguration configuration, bool addHostedServices = true)
    {
        var connectionString = configuration.GetConnectionString("Default");
        var useSqlServer = !string.IsNullOrWhiteSpace(connectionString);

        services.AddDbContext<LeaseVaultDbContext>(options =>
        {
            if (useSqlServer)
            {
                options.UseSqlServer(connectionString, sql => sql.EnableRetryOnFailure());
            }
            else
            {
                options.UseSqlite("Data Source=app.db");
            }
        });

        services.Configure<StorageOptions>(configuration.GetSection(StorageOptions.SectionName));
        services.Configure<SearchOptions>(configuration.GetSection(SearchOptions.SectionName));
        services.Configure<ApprovalOptions>(configuration.GetSection(ApprovalOptions.SectionName));
        services.Configure<ReminderOptions>(configuration.GetSection(ReminderOptions.SectionName));
        services.Configure<RetentionOptions>(configuration.GetSection(RetentionOptions.SectionName));

        services.AddSingleton<IClock, SystemClock>();

        // Storage: singleton, both implementations are thread-safe.
        var storageOptions = configuration.GetSection(StorageOptions.SectionName).Get<StorageOptions>() ?? new StorageOptions();
        if (storageOptions.UseAzureBlob)
        {
            services.AddSingleton<IDocumentStorage, AzureBlobDocumentStorage>();
        }
        else
        {
            services.AddSingleton<IDocumentStorage, LocalDiskDocumentStorage>();
        }

        // Search: scoped because it shares the request's DbContext.
        services.AddScoped<ISearchService>(sp =>
        {
            var db = sp.GetRequiredService<LeaseVaultDbContext>();
            var options = sp.GetRequiredService<IOptions<SearchOptions>>().Value;
            return db.IsSqlite
                ? new SqliteFtsSearchService(db, options, sp.GetRequiredService<ILogger<SqliteFtsSearchService>>())
                : new SqlServerFullTextSearchService(db, options, sp.GetRequiredService<ILogger<SqlServerFullTextSearchService>>());
        });

        services.AddScoped<IAuditLog, EfAuditLog>();
        services.AddSingleton<IEmailSender, LoggingEmailSender>();
        services.AddSingleton<IApproverDirectory, ConfiguredApproverDirectory>();

        services.AddScoped<DocumentService>();
        services.AddScoped<ApprovalService>();
        services.AddScoped<RetentionService>();
        services.AddScoped<LeaseExpiryNotifier>();
        services.AddScoped<DbSeeder>();

        if (addHostedServices)
        {
            services.AddHostedService<LeaseExpiryReminderService>();
            services.AddHostedService<ApprovalEscalationService>();
            services.AddHostedService<RetentionSweepService>();
        }

        return services;
    }
}
