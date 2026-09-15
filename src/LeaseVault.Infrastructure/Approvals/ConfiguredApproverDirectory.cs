using LeaseVault.Core.Abstractions;
using LeaseVault.Core.Approvals;
using LeaseVault.Core.Domain;
using Microsoft.Extensions.Options;

namespace LeaseVault.Infrastructure.Approvals;

/// <summary>Bound from the "Approvals" section.</summary>
public sealed class ApprovalOptions
{
    public const string SectionName = "Approvals";

    /// <summary>Hours an approver has before the step escalates.</summary>
    public int StepSlaHours { get; set; } = 48;

    /// <summary>How often the escalation service scans for overdue steps.</summary>
    public int EscalationPollMinutes { get; set; } = 15;

    public string DefaultAgent { get; set; } = "agent@leasevault.local";
    public string DefaultLegal { get; set; } = "legal@leasevault.local";
    public string DefaultOwner { get; set; } = "owner@leasevault.local";

    /// <summary>Escalation contact once the Owner step is overdue (usually an administrator).</summary>
    public string FallbackOwner { get; set; } = "admin@leasevault.local";
}

/// <summary>
/// Resolves approvers from configuration. In production these values point at Entra ID users
/// or distribution lists; the same object shape lets the UI override assignments per document.
/// </summary>
public sealed class ConfiguredApproverDirectory : IApproverDirectory
{
    private readonly ApprovalOptions _options;

    public ConfiguredApproverDirectory(IOptions<ApprovalOptions> options)
    {
        _options = options.Value;
    }

    public Task<ApprovalChainAssignments> GetDefaultAssignmentsAsync(Document document, CancellationToken ct = default) =>
        Task.FromResult(new ApprovalChainAssignments(_options.DefaultAgent, _options.DefaultLegal, _options.DefaultOwner));

    // User names are e-mail addresses in both the dev directory and Entra ID (UPN).
    public string EmailFor(string userName) => userName.Contains('@') ? userName : $"{userName}@leasevault.local";
}
