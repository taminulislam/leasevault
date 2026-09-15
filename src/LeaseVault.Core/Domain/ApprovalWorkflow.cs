using System.ComponentModel.DataAnnotations;
using LeaseVault.Core.Approvals;

namespace LeaseVault.Core.Domain;

/// <summary>
/// A sequential approval chain over a document. All rule enforcement lives here so the
/// application layer only orchestrates persistence, notifications and auditing.
/// </summary>
public class ApprovalWorkflow
{
    public int Id { get; set; }

    public int DocumentId { get; set; }
    public Document? Document { get; set; }

    public WorkflowStatus Status { get; set; } = WorkflowStatus.InProgress;

    [Required, MaxLength(200)]
    public string StartedBy { get; set; } = string.Empty;

    public DateTime StartedUtc { get; set; }

    public DateTime? CompletedUtc { get; set; }

    /// <summary>SLA per step; when exceeded the escalation service moves the step up the chain.</summary>
    public int StepSlaHours { get; set; } = 48;

    /// <summary>Last-resort escalation contact once the Owner step itself is overdue.</summary>
    [MaxLength(200)]
    public string? FallbackOwner { get; set; }

    public ICollection<ApprovalStep> Steps { get; set; } = new List<ApprovalStep>();

    public IEnumerable<ApprovalStep> OrderedSteps => Steps.OrderBy(s => s.Order);

    public ApprovalStep? CurrentStep => OrderedSteps.FirstOrDefault(s => s.Status == StepStatus.Pending);

    public bool IsComplete => Status != WorkflowStatus.InProgress;

    public TimeSpan StepSla => TimeSpan.FromHours(StepSlaHours);

    // ---- Factory ---------------------------------------------------------------------------

    public static ApprovalWorkflow Start(
        Document document,
        string startedBy,
        ApprovalChainAssignments assignments,
        DateTime nowUtc,
        int stepSlaHours = 48,
        string? fallbackOwner = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(assignments);
        Guard.NotEmpty(startedBy, nameof(startedBy));

        if (document.CurrentVersion == 0)
        {
            throw new DomainException("Upload a version before submitting the document for approval.");
        }

        if (document.Status is DocumentStatus.Archived or DocumentStatus.Disposed)
        {
            throw new DomainException($"Document is {document.Status} and cannot be submitted for approval.");
        }

        if (document.Workflows.Any(w => w.Status == WorkflowStatus.InProgress))
        {
            throw new DomainException("An approval workflow is already in progress for this document.");
        }

        if (document.IsCheckedOut)
        {
            throw new DomainException("Check the document in before submitting it for approval.");
        }

        if (stepSlaHours <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(stepSlaHours));
        }

        var workflow = new ApprovalWorkflow
        {
            DocumentId = document.Id,
            Document = document,
            StartedBy = startedBy,
            StartedUtc = nowUtc,
            StepSlaHours = stepSlaHours,
            FallbackOwner = fallbackOwner ?? assignments.Owner
        };

        var order = 1;
        foreach (var role in ApprovalChain.Roles)
        {
            workflow.Steps.Add(new ApprovalStep
            {
                Order = order++,
                Role = role,
                Assignee = assignments.Resolve(role),
                Status = StepStatus.Waiting
            });
        }

        workflow.OrderedSteps.First().Activate(nowUtc, workflow.StepSla);
        document.Status = DocumentStatus.PendingApproval;
        document.Workflows.Add(workflow);
        return workflow;
    }

    // ---- Decisions -------------------------------------------------------------------------

    public ApprovalStep Approve(string user, DateTime nowUtc, string? comment = null)
    {
        var step = RequireActionableStep(user);
        step.Decide(StepStatus.Approved, user, nowUtc, comment);

        var next = OrderedSteps.FirstOrDefault(s => s.Order > step.Order && s.Status == StepStatus.Waiting);
        if (next is null)
        {
            Complete(WorkflowStatus.Approved, nowUtc);
        }
        else
        {
            next.Activate(nowUtc, StepSla);
        }

        return step;
    }

    public ApprovalStep Reject(string user, DateTime nowUtc, string? comment = null)
    {
        var step = RequireActionableStep(user);
        if (string.IsNullOrWhiteSpace(comment))
        {
            throw new DomainException("A comment is required when rejecting.");
        }

        step.Decide(StepStatus.Rejected, user, nowUtc, comment);
        foreach (var remaining in OrderedSteps.Where(s => s.Status == StepStatus.Waiting))
        {
            remaining.Status = StepStatus.Skipped;
        }

        Complete(WorkflowStatus.Rejected, nowUtc);
        return step;
    }

    /// <summary>The current approver hands the decision to a colleague (they both remain able to act).</summary>
    public ApprovalStep Delegate(string byUser, string toUser, DateTime nowUtc)
    {
        Guard.NotEmpty(toUser, nameof(toUser));
        var step = RequireActionableStep(byUser);

        if (string.Equals(byUser, toUser, StringComparison.OrdinalIgnoreCase))
        {
            throw new DomainException("You cannot delegate a step to yourself.");
        }

        step.DelegatedTo = toUser;
        step.DueUtc = nowUtc + StepSla; // the delegate receives a fresh SLA window
        return step;
    }

    /// <summary>
    /// Time-based escalation. When the pending step is overdue it is escalated to the assignee
    /// of the next role in the chain, and finally to the fallback owner. Returns the escalated
    /// step, or null when nothing was overdue.
    /// </summary>
    public ApprovalStep? EscalateIfOverdue(DateTime nowUtc)
    {
        var step = CurrentStep;
        if (step is null || !step.IsOverdue(nowUtc))
        {
            return null;
        }

        var target = ResolveEscalationTarget(step);
        if (target is null)
        {
            return null; // nobody further up the chain; leave for manual intervention
        }

        step.EscalatedTo = target;
        step.EscalationCount++;
        step.DueUtc = nowUtc + StepSla;
        return step;
    }

    public void Cancel(string user, DateTime nowUtc)
    {
        if (IsComplete)
        {
            throw new DomainException($"Workflow is already {Status}.");
        }

        foreach (var s in Steps.Where(s => s.Status is StepStatus.Pending or StepStatus.Waiting))
        {
            s.Status = StepStatus.Skipped;
        }

        Complete(WorkflowStatus.Cancelled, nowUtc);
        if (Document is not null)
        {
            Document.Status = DocumentStatus.Draft;
        }
    }

    // ---- Helpers ---------------------------------------------------------------------------

    private string? ResolveEscalationTarget(ApprovalStep step)
    {
        // Walk up the chain: 1st escalation -> next role's assignee, 2nd -> the one after, ...
        var laterSteps = OrderedSteps.Where(s => s.Order > step.Order).ToList();
        var index = step.EscalationCount;
        if (index < laterSteps.Count)
        {
            return laterSteps[index].Assignee;
        }

        // Past the Owner: escalate to the fallback owner once.
        if (!string.IsNullOrEmpty(FallbackOwner)
            && !string.Equals(step.EscalatedTo, FallbackOwner, StringComparison.OrdinalIgnoreCase))
        {
            return FallbackOwner;
        }

        return null;
    }

    private ApprovalStep RequireActionableStep(string user)
    {
        Guard.NotEmpty(user, nameof(user));

        if (IsComplete)
        {
            throw new DomainException($"Workflow is already {Status}; no further decisions can be recorded.");
        }

        var step = CurrentStep ?? throw new DomainException("Workflow has no pending step.");
        if (!step.CanActBy(user))
        {
            throw new NotPermittedException(
                $"'{user}' is not an approver for the {step.Role} step (assigned to '{step.Assignee}').");
        }

        return step;
    }

    private void Complete(WorkflowStatus outcome, DateTime nowUtc)
    {
        Status = outcome;
        CompletedUtc = nowUtc;

        if (Document is null)
        {
            return;
        }

        Document.Status = outcome switch
        {
            WorkflowStatus.Approved => DocumentStatus.Approved,
            WorkflowStatus.Rejected => DocumentStatus.Rejected,
            _ => Document.Status
        };
    }
}
