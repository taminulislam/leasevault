namespace LeaseVault.Core.Abstractions;

/// <summary>Append-only audit trail. Entries can be written and read, never changed.</summary>
public interface IAuditLog
{
    Task AppendAsync(string action, string entityType, object? entityId, string? details = null, CancellationToken ct = default);
}

/// <summary>Well-known audit action names so reports and tests share one vocabulary.</summary>
public static class AuditActions
{
    public const string Created = "Created";
    public const string Updated = "Updated";
    public const string CheckedOut = "CheckedOut";
    public const string CheckedIn = "CheckedIn";
    public const string VersionAdded = "VersionAdded";
    public const string Downloaded = "Downloaded";
    public const string WorkflowStarted = "WorkflowStarted";
    public const string StepApproved = "StepApproved";
    public const string StepRejected = "StepRejected";
    public const string StepDelegated = "StepDelegated";
    public const string StepEscalated = "StepEscalated";
    public const string WorkflowCancelled = "WorkflowCancelled";
    public const string Archived = "Archived";
    public const string Disposed = "Disposed";
    public const string ReminderSent = "ReminderSent";
    public const string SignedIn = "SignedIn";
}
