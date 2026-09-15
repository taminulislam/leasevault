using LeaseVault.Core.Domain;

namespace LeaseVault.Core.Retention;

/// <summary>
/// Pure retention rules: when does a document become eligible for disposal and what should happen.
/// </summary>
public static class RetentionCalculator
{
    /// <summary>
    /// The event the retention clock starts from: the lease end date for lease-bound documents,
    /// otherwise the day the document was created.
    /// </summary>
    public static DateOnly TriggerDate(Document document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return document.Lease is not null
            ? document.Lease.EndDate
            : DateOnly.FromDateTime(document.CreatedUtc);
    }

    public static DateOnly DisposalDate(Document document, RetentionPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(policy);
        return TriggerDate(document).AddYears(policy.RetentionYears);
    }

    /// <summary>
    /// True when the document has passed its retention period and may be archived/deleted.
    /// Documents already archived/disposed, under legal hold or checked out are never due.
    /// </summary>
    public static bool IsDue(Document document, RetentionPolicy policy, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(policy);

        if (policy.LegalHold)
        {
            return false;
        }

        if (document.Status is DocumentStatus.Disposed)
        {
            return false;
        }

        if (document.Status is DocumentStatus.Archived && policy.Action == DisposalAction.Archive)
        {
            return false; // already in the terminal state for this policy
        }

        if (document.IsCheckedOut)
        {
            return false;
        }

        // Never dispose of a document whose lease is still running, regardless of the dates.
        if (document.Lease is not null && document.Lease.Status is LeaseStatus.Active or LeaseStatus.Expiring)
        {
            return false;
        }

        return DisposalDate(document, policy) <= today;
    }

    /// <summary>Picks the most specific policy for a document: explicit assignment, then category match, then catch-all.</summary>
    public static RetentionPolicy? ResolvePolicy(Document document, IEnumerable<RetentionPolicy> policies)
    {
        ArgumentNullException.ThrowIfNull(document);
        var list = policies.ToList();

        if (document.RetentionPolicy is not null)
        {
            return document.RetentionPolicy;
        }

        if (document.RetentionPolicyId is int id && list.FirstOrDefault(p => p.Id == id) is { } assigned)
        {
            return assigned;
        }

        return list.FirstOrDefault(p => p.Category == document.Category)
               ?? list.FirstOrDefault(p => p.Category is null);
    }
}
