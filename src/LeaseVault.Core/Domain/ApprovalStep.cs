using System.ComponentModel.DataAnnotations;

namespace LeaseVault.Core.Domain;

/// <summary>
/// One link in the Agent -> Legal -> Owner chain. Exactly one step is Pending at a time.
/// </summary>
public class ApprovalStep
{
    public int Id { get; set; }

    public int WorkflowId { get; set; }
    public ApprovalWorkflow? Workflow { get; set; }

    /// <summary>1-based position in the chain.</summary>
    public int Order { get; set; }

    public ApprovalRole Role { get; set; }

    /// <summary>Primary approver for this step.</summary>
    [Required, MaxLength(200)]
    public string Assignee { get; set; } = string.Empty;

    /// <summary>User the assignee delegated the decision to, if any.</summary>
    [MaxLength(200)]
    public string? DelegatedTo { get; set; }

    /// <summary>User the step was escalated to after the SLA lapsed, if any.</summary>
    [MaxLength(200)]
    public string? EscalatedTo { get; set; }

    public StepStatus Status { get; set; } = StepStatus.Waiting;

    public DateTime? ActivatedUtc { get; set; }

    public DateTime? DueUtc { get; set; }

    public DateTime? DecidedUtc { get; set; }

    [MaxLength(200)]
    public string? DecidedBy { get; set; }

    [MaxLength(1000)]
    public string? Comment { get; set; }

    public int EscalationCount { get; set; }

    public bool IsOverdue(DateTime nowUtc) => Status == StepStatus.Pending && DueUtc is not null && DueUtc < nowUtc;

    /// <summary>Everyone currently entitled to decide this step.</summary>
    public IEnumerable<string> Actors
    {
        get
        {
            yield return Assignee;
            if (!string.IsNullOrEmpty(DelegatedTo))
            {
                yield return DelegatedTo;
            }

            if (!string.IsNullOrEmpty(EscalatedTo))
            {
                yield return EscalatedTo;
            }
        }
    }

    public bool CanActBy(string user) =>
        Actors.Any(a => string.Equals(a, user, StringComparison.OrdinalIgnoreCase));

    internal void Activate(DateTime nowUtc, TimeSpan sla)
    {
        Status = StepStatus.Pending;
        ActivatedUtc = nowUtc;
        DueUtc = nowUtc + sla;
    }

    internal void Decide(StepStatus outcome, string user, DateTime nowUtc, string? comment)
    {
        Status = outcome;
        DecidedBy = user;
        DecidedUtc = nowUtc;
        Comment = comment;
    }
}
