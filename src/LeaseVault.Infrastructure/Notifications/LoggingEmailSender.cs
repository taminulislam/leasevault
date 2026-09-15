using LeaseVault.Core.Abstractions;
using Microsoft.Extensions.Logging;

namespace LeaseVault.Infrastructure.Notifications;

/// <summary>
/// Console/log fallback used when no mail provider is configured. Production swaps this for an
/// SMTP or SendGrid implementation via <c>Email:Provider</c>.
/// </summary>
public sealed class LoggingEmailSender : IEmailSender
{
    private readonly ILogger<LoggingEmailSender> _logger;

    public LoggingEmailSender(ILogger<LoggingEmailSender> logger)
    {
        _logger = logger;
    }

    public Task SendAsync(EmailMessage message, CancellationToken ct = default)
    {
        _logger.LogInformation("EMAIL to {To} | {Subject} | {Body}", message.To, message.Subject, message.Body);
        return Task.CompletedTask;
    }
}
