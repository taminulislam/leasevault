namespace LeaseVault.Core.Abstractions;

/// <summary>Outbound notification abstraction (SMTP/SendGrid in production, console/log locally).</summary>
public interface IEmailSender
{
    Task SendAsync(EmailMessage message, CancellationToken ct = default);
}

public sealed record EmailMessage(string To, string Subject, string Body)
{
    public string? Cc { get; init; }
}
