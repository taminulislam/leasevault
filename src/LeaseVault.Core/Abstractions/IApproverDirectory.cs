using LeaseVault.Core.Approvals;
using LeaseVault.Core.Domain;

namespace LeaseVault.Core.Abstractions;

/// <summary>
/// Resolves who should approve a document at each role. Backed by configuration
/// (or Entra ID group membership in production).
/// </summary>
public interface IApproverDirectory
{
    Task<ApprovalChainAssignments> GetDefaultAssignmentsAsync(Document document, CancellationToken ct = default);

    /// <summary>Address used for reminder / escalation e-mails sent to a user name.</summary>
    string EmailFor(string userName);
}
