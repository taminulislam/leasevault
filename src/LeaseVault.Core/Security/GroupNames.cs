using LeaseVault.Core.Domain;

namespace LeaseVault.Core.Security;

/// <summary>
/// Application security groups. In Entra ID these map to group object ids via configuration;
/// in development they are plain names attached to the dev cookie.
/// </summary>
public static class GroupNames
{
    public const string Agents = "Agents";
    public const string Legal = "Legal";
    public const string Owners = "Owners";
    public const string Admins = "Admins";

    public static readonly IReadOnlyList<string> All = [Agents, Legal, Owners, Admins];

    public static string ForRole(ApprovalRole role) => role switch
    {
        ApprovalRole.Agent => Agents,
        ApprovalRole.Legal => Legal,
        ApprovalRole.Owner => Owners,
        _ => throw new ArgumentOutOfRangeException(nameof(role))
    };
}

/// <summary>Authorization policy names registered in the web host.</summary>
public static class Policies
{
    public const string Agents = "RequireAgents";
    public const string Legal = "RequireLegal";
    public const string Owners = "RequireOwners";
    public const string Admins = "RequireAdmins";
    public const string Approvers = "RequireApprover";  // any of Agents / Legal / Owners / Admins
    public const string Contributors = "RequireContributor"; // Agents or Admins (may upload / check out)
}
