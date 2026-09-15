namespace LeaseVault.Core.Abstractions;

/// <summary>Identity of the caller, resolved from Entra ID claims or the dev cookie.</summary>
public interface ICurrentUser
{
    bool IsAuthenticated { get; }

    /// <summary>Stable user name (UPN / e-mail) used for locks, assignments and audit rows.</summary>
    string Name { get; }

    string DisplayName { get; }

    /// <summary>Application group names the user belongs to (Agents, Legal, Owners, Admins).</summary>
    IReadOnlyCollection<string> Groups { get; }

    bool IsInGroup(string group) => Groups.Contains(group, StringComparer.OrdinalIgnoreCase);
}
