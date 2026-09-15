using LeaseVault.Core.Domain;

namespace LeaseVault.Core.Approvals;

/// <summary>
/// Defines the fixed order of the approval chain: Agent -> Legal -> Owner.
/// </summary>
public static class ApprovalChain
{
    public static readonly IReadOnlyList<ApprovalRole> Roles =
    [
        ApprovalRole.Agent,
        ApprovalRole.Legal,
        ApprovalRole.Owner
    ];

    /// <summary>The role that a step of <paramref name="role"/> escalates to, or null for the last role.</summary>
    public static ApprovalRole? NextRole(ApprovalRole role)
    {
        var index = Roles.ToList().IndexOf(role);
        return index >= 0 && index < Roles.Count - 1 ? Roles[index + 1] : null;
    }
}

/// <summary>Who is responsible for each role in a specific workflow instance.</summary>
public sealed record ApprovalChainAssignments(string Agent, string Legal, string Owner)
{
    public string Resolve(ApprovalRole role) => role switch
    {
        ApprovalRole.Agent => Require(Agent, role),
        ApprovalRole.Legal => Require(Legal, role),
        ApprovalRole.Owner => Require(Owner, role),
        _ => throw new ArgumentOutOfRangeException(nameof(role))
    };

    private static string Require(string value, ApprovalRole role) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new DomainException($"No approver assigned for the {role} step.")
            : value;
}
