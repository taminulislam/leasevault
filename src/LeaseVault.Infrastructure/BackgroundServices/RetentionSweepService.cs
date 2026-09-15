using LeaseVault.Infrastructure.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LeaseVault.Infrastructure.BackgroundServices;

/// <summary>Bound from the "Retention" section.</summary>
public sealed class RetentionOptions
{
    public const string SectionName = "Retention";

    public bool Enabled { get; set; } = true;

    public int PollHours { get; set; } = 24;
}

/// <summary>
/// Hosted service that applies retention policies on a schedule (archive / dispose of documents
/// whose retention period elapsed).
/// </summary>
public sealed class RetentionSweepService : BackgroundService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly IOptionsMonitor<RetentionOptions> _options;
    private readonly ILogger<RetentionSweepService> _logger;

    public RetentionSweepService(IServiceScopeFactory scopes, IOptionsMonitor<RetentionOptions> options, ILogger<RetentionSweepService> logger)
    {
        _scopes = scopes;
        _options = options;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.CurrentValue.Enabled)
        {
            _logger.LogInformation("Retention sweep service disabled by configuration");
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopes.CreateScope();
                var retention = scope.ServiceProvider.GetRequiredService<RetentionService>();
                await retention.SweepAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Retention sweep failed");
            }

            try
            {
                await Task.Delay(TimeSpan.FromHours(Math.Max(1, _options.CurrentValue.PollHours)), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }
}
