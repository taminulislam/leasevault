using LeaseVault.Infrastructure.Approvals;
using LeaseVault.Infrastructure.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LeaseVault.Infrastructure.BackgroundServices;

/// <summary>
/// Hosted service that escalates overdue approval steps to the next approver in the chain
/// (Agent -> Legal -> Owner -> fallback owner). Poll interval and SLA come from
/// <see cref="ApprovalOptions"/> ("Approvals" configuration section).
/// </summary>
public sealed class ApprovalEscalationService : BackgroundService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly IOptionsMonitor<ApprovalOptions> _options;
    private readonly ILogger<ApprovalEscalationService> _logger;

    public ApprovalEscalationService(IServiceScopeFactory scopes, IOptionsMonitor<ApprovalOptions> options, ILogger<ApprovalEscalationService> logger)
    {
        _scopes = scopes;
        _options = options;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Approval escalation service started (SLA {Sla}h, every {Poll} min)",
            _options.CurrentValue.StepSlaHours, _options.CurrentValue.EscalationPollMinutes);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopes.CreateScope();
                var approvals = scope.ServiceProvider.GetRequiredService<ApprovalService>();
                var escalated = await approvals.EscalateOverdueAsync(stoppingToken);
                if (escalated > 0)
                {
                    _logger.LogInformation("Escalated {Count} overdue approval step(s)", escalated);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Approval escalation run failed");
            }

            var delay = TimeSpan.FromMinutes(Math.Max(1, _options.CurrentValue.EscalationPollMinutes));
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
