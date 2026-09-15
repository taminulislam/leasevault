using LeaseVault.Core.Abstractions;
using LeaseVault.Core.Approvals;
using LeaseVault.Core.Domain;
using LeaseVault.Infrastructure.Approvals;
using LeaseVault.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LeaseVault.Infrastructure.Services;

/// <summary>
/// Orchestrates the approval chain: persistence, e-mail notifications and audit entries around
/// the rules implemented by <see cref="ApprovalWorkflow"/>.
/// </summary>
public sealed class ApprovalService
{
    private readonly LeaseVaultDbContext _db;
    private readonly IApproverDirectory _directory;
    private readonly IEmailSender _email;
    private readonly IAuditLog _audit;
    private readonly ICurrentUser _user;
    private readonly IClock _clock;
    private readonly ApprovalOptions _options;
    private readonly ILogger<ApprovalService> _logger;

    public ApprovalService(
        LeaseVaultDbContext db,
        IApproverDirectory directory,
        IEmailSender email,
        IAuditLog audit,
        ICurrentUser user,
        IClock clock,
        IOptions<ApprovalOptions> options,
        ILogger<ApprovalService> logger)
    {
        _db = db;
        _directory = directory;
        _email = email;
        _audit = audit;
        _user = user;
        _clock = clock;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<ApprovalWorkflow> StartAsync(int documentId, ApprovalChainAssignments? assignments = null, CancellationToken ct = default)
    {
        var document = await _db.Documents
            .Include(d => d.Workflows).ThenInclude(w => w.Steps)
            .FirstOrDefaultAsync(d => d.Id == documentId, ct)
            ?? throw new KeyNotFoundException($"Document {documentId} not found.");

        assignments ??= await _directory.GetDefaultAssignmentsAsync(document, ct);
        var workflow = ApprovalWorkflow.Start(document, _user.Name, assignments, _clock.UtcNow, _options.StepSlaHours, _options.FallbackOwner);

        await _audit.AppendAsync(AuditActions.WorkflowStarted, nameof(Document), documentId, $"Chain {assignments.Agent} -> {assignments.Legal} -> {assignments.Owner}", ct);
        await _db.SaveChangesAsync(ct);

        await NotifyCurrentStepAsync(workflow, "Approval requested", ct);
        return workflow;
    }

    public async Task<ApprovalWorkflow> ApproveAsync(int workflowId, string? comment, CancellationToken ct = default)
    {
        var workflow = await LoadAsync(workflowId, ct);
        var step = workflow.Approve(_user.Name, _clock.UtcNow, comment);

        await _audit.AppendAsync(AuditActions.StepApproved, nameof(ApprovalWorkflow), workflowId, $"{step.Role} step approved by {_user.Name}", ct);
        await _db.SaveChangesAsync(ct);

        if (workflow.IsComplete)
        {
            await _email.SendAsync(new EmailMessage(_directory.EmailFor(workflow.StartedBy),
                $"Document '{workflow.Document!.Title}' approved",
                "All approval steps are complete."), ct);
        }
        else
        {
            await NotifyCurrentStepAsync(workflow, "Approval requested", ct);
        }

        return workflow;
    }

    public async Task<ApprovalWorkflow> RejectAsync(int workflowId, string comment, CancellationToken ct = default)
    {
        var workflow = await LoadAsync(workflowId, ct);
        var step = workflow.Reject(_user.Name, _clock.UtcNow, comment);

        await _audit.AppendAsync(AuditActions.StepRejected, nameof(ApprovalWorkflow), workflowId, $"{step.Role} step rejected by {_user.Name}: {comment}", ct);
        await _db.SaveChangesAsync(ct);

        await _email.SendAsync(new EmailMessage(_directory.EmailFor(workflow.StartedBy),
            $"Document '{workflow.Document!.Title}' rejected",
            $"Rejected at the {step.Role} step by {_user.Name}: {comment}"), ct);
        return workflow;
    }

    public async Task<ApprovalWorkflow> DelegateAsync(int workflowId, string toUser, CancellationToken ct = default)
    {
        var workflow = await LoadAsync(workflowId, ct);
        var step = workflow.Delegate(_user.Name, toUser, _clock.UtcNow);

        await _audit.AppendAsync(AuditActions.StepDelegated, nameof(ApprovalWorkflow), workflowId, $"{step.Role} step delegated by {_user.Name} to {toUser}", ct);
        await _db.SaveChangesAsync(ct);

        await _email.SendAsync(new EmailMessage(_directory.EmailFor(toUser),
            $"Approval delegated to you: '{workflow.Document!.Title}'",
            $"{_user.Name} delegated the {step.Role} approval to you. Due {step.DueUtc:u}."), ct);
        return workflow;
    }

    public async Task CancelAsync(int workflowId, CancellationToken ct = default)
    {
        var workflow = await LoadAsync(workflowId, ct);
        workflow.Cancel(_user.Name, _clock.UtcNow);
        await _audit.AppendAsync(AuditActions.WorkflowCancelled, nameof(ApprovalWorkflow), workflowId, null, ct);
        await _db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Time-based escalation used by <c>ApprovalEscalationService</c>: every in-progress workflow
    /// whose pending step passed its SLA is escalated one level up the chain and the new
    /// approver is notified. Returns the number of steps escalated.
    /// </summary>
    public async Task<int> EscalateOverdueAsync(CancellationToken ct = default)
    {
        var now = _clock.UtcNow;
        var overdue = await _db.ApprovalWorkflows
            .Include(w => w.Document)
            .Include(w => w.Steps)
            .Where(w => w.Status == WorkflowStatus.InProgress
                        && w.Steps.Any(s => s.Status == StepStatus.Pending && s.DueUtc < now))
            .ToListAsync(ct);

        var count = 0;
        foreach (var workflow in overdue)
        {
            var step = workflow.EscalateIfOverdue(now);
            if (step is null)
            {
                continue;
            }

            count++;
            await _audit.AppendAsync(AuditActions.StepEscalated, nameof(ApprovalWorkflow), workflow.Id,
                $"{step.Role} step overdue; escalated (#{step.EscalationCount}) to {step.EscalatedTo}", ct);

            await _email.SendAsync(new EmailMessage(_directory.EmailFor(step.EscalatedTo!),
                $"Escalated approval: '{workflow.Document!.Title}'",
                $"The {step.Role} approval assigned to {step.Assignee} is overdue and has been escalated to you. New due date {step.DueUtc:u}."), ct);

            _logger.LogWarning("Workflow {WorkflowId} step {Role} escalated to {Target}", workflow.Id, step.Role, step.EscalatedTo);
        }

        if (count > 0)
        {
            await _db.SaveChangesAsync(ct);
        }

        return count;
    }

    private async Task NotifyCurrentStepAsync(ApprovalWorkflow workflow, string subjectPrefix, CancellationToken ct)
    {
        var step = workflow.CurrentStep;
        if (step is null)
        {
            return;
        }

        await _email.SendAsync(new EmailMessage(_directory.EmailFor(step.Assignee),
            $"{subjectPrefix}: '{workflow.Document?.Title}' ({step.Role})",
            $"Please review document #{workflow.DocumentId}. Due {step.DueUtc:u}."), ct);
    }

    private async Task<ApprovalWorkflow> LoadAsync(int workflowId, CancellationToken ct) =>
        await _db.ApprovalWorkflows
            .Include(w => w.Document)
            .Include(w => w.Steps)
            .FirstOrDefaultAsync(w => w.Id == workflowId, ct)
        ?? throw new KeyNotFoundException($"Workflow {workflowId} not found.");
}
