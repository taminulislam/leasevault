using LeaseVault.Infrastructure.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LeaseVault.Infrastructure.BackgroundServices;

/// <summary>
/// Hosted service that periodically sends lease-expiry reminders. The lead window and poll
/// interval come from <see cref="ReminderOptions"/> ("Reminders" configuration section).
/// </summary>
public sealed class LeaseExpiryReminderService : BackgroundService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly IOptionsMonitor<ReminderOptions> _options;
    private readonly ILogger<LeaseExpiryReminderService> _logger;

    public LeaseExpiryReminderService(IServiceScopeFactory scopes, IOptionsMonitor<ReminderOptions> options, ILogger<LeaseExpiryReminderService> logger)
    {
        _scopes = scopes;
        _options = options;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Lease expiry reminder service started (lead {LeadDays} days, every {Poll} min)",
            _options.CurrentValue.LeadDays, _options.CurrentValue.PollMinutes);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopes.CreateScope();
                var notifier = scope.ServiceProvider.GetRequiredService<LeaseExpiryNotifier>();
                await notifier.SendDueRemindersAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lease expiry reminder run failed");
            }

            var delay = TimeSpan.FromMinutes(Math.Max(1, _options.CurrentValue.PollMinutes));
            try
            {
                await Task.Delay(delay, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }
}
