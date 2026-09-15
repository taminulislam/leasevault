namespace LeaseVault.Core.Domain;

public enum LeaseStatus
{
    Draft = 0,
    Active = 1,
    Expiring = 2,
    Expired = 3,
    Terminated = 4,
    Renewed = 5
}

public enum DocumentCategory
{
    Lease = 0,
    Amendment = 1,
    Insurance = 2,
    Inspection = 3,
    Correspondence = 4,
    Invoice = 5,
    Other = 6
}

public enum DocumentStatus
{
    Draft = 0,
    PendingApproval = 1,
    Approved = 2,
    Rejected = 3,
    Archived = 4,
    Disposed = 5
}

public enum ApprovalRole
{
    Agent = 0,
    Legal = 1,
    Owner = 2
}

public enum WorkflowStatus
{
    InProgress = 0,
    Approved = 1,
    Rejected = 2,
    Cancelled = 3
}

public enum StepStatus
{
    Waiting = 0,
    Pending = 1,
    Approved = 2,
    Rejected = 3,
    Skipped = 4
}

public enum DisposalAction
{
    Archive = 0,
    Delete = 1
}
