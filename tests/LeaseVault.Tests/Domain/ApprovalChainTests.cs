using LeaseVault.Core.Approvals;
using LeaseVault.Core.Domain;
using LeaseVault.Tests.Support;

namespace LeaseVault.Tests.Domain;

public class ApprovalChainTests
{
    private static readonly ApprovalChainAssignments Chain = new("agent@x", "legal@x", "owner@x");
    private static readonly DateTime T0 = TestData.Now;

    private static ApprovalWorkflow StartWorkflow(Document? doc = null) =>
        ApprovalWorkflow.Start(doc ?? TestData.NewDocument(), "agent@x", Chain, T0, stepSlaHours: 24, fallbackOwner: "admin@x");

    [Fact]
    public void Start_creates_agent_legal_owner_steps_in_order_and_activates_first()
    {
        var doc = TestData.NewDocument();
        var wf = StartWorkflow(doc);

        Assert.Equal([ApprovalRole.Agent, ApprovalRole.Legal, ApprovalRole.Owner], wf.OrderedSteps.Select(s => s.Role));
        Assert.Equal(ApprovalRole.Agent, wf.CurrentStep!.Role);
        Assert.Equal(StepStatus.Pending, wf.CurrentStep.Status);
        Assert.Equal(T0.AddHours(24), wf.CurrentStep.DueUtc);
        Assert.All(wf.OrderedSteps.Skip(1), s => Assert.Equal(StepStatus.Waiting, s.Status));
        Assert.Equal(DocumentStatus.PendingApproval, doc.Status);
    }

    [Fact]
    public void Start_requires_uploaded_content()
    {
        var doc = TestData.NewDocument(versions: 0);
        var ex = Assert.Throws<DomainException>(() => StartWorkflow(doc));
        Assert.Contains("Upload a version", ex.Message);
    }

    [Fact]
    public void Start_rejects_second_concurrent_workflow()
    {
        var doc = TestData.NewDocument();
        StartWorkflow(doc);
        Assert.Throws<DomainException>(() => StartWorkflow(doc));
    }

    [Fact]
    public void Start_rejects_checked_out_document()
    {
        var doc = TestData.NewDocument();
        doc.CheckOut("agent@x", T0);
        Assert.Throws<DomainException>(() => StartWorkflow(doc));
    }

    [Fact]
    public void Approve_advances_through_chain_and_approves_document()
    {
        var doc = TestData.NewDocument();
        var wf = StartWorkflow(doc);

        wf.Approve("agent@x", T0.AddHours(1));
        Assert.Equal(ApprovalRole.Legal, wf.CurrentStep!.Role);

        wf.Approve("legal@x", T0.AddHours(2));
        Assert.Equal(ApprovalRole.Owner, wf.CurrentStep!.Role);

        wf.Approve("owner@x", T0.AddHours(3), "Signed off");
        Assert.Null(wf.CurrentStep);
        Assert.Equal(WorkflowStatus.Approved, wf.Status);
        Assert.Equal(T0.AddHours(3), wf.CompletedUtc);
        Assert.Equal(DocumentStatus.Approved, doc.Status);
        Assert.All(wf.Steps, s => Assert.Equal(StepStatus.Approved, s.Status));
    }

    [Fact]
    public void Only_the_current_steps_assignee_may_decide()
    {
        var wf = StartWorkflow();

        var ex = Assert.Throws<NotPermittedException>(() => wf.Approve("legal@x", T0));
        Assert.Contains("not an approver", ex.Message);
        Assert.Throws<NotPermittedException>(() => wf.Approve("owner@x", T0));
        Assert.Equal(ApprovalRole.Agent, wf.CurrentStep!.Role);
    }

    [Fact]
    public void Reject_requires_comment_ends_workflow_and_skips_remaining_steps()
    {
        var doc = TestData.NewDocument();
        var wf = StartWorkflow(doc);
        wf.Approve("agent@x", T0);

        Assert.Throws<DomainException>(() => wf.Reject("legal@x", T0, comment: " "));

        wf.Reject("legal@x", T0.AddHours(5), "Indemnity clause missing");

        Assert.Equal(WorkflowStatus.Rejected, wf.Status);
        Assert.Equal(DocumentStatus.Rejected, doc.Status);
        Assert.Equal(StepStatus.Rejected, wf.OrderedSteps.ElementAt(1).Status);
        Assert.Equal(StepStatus.Skipped, wf.OrderedSteps.ElementAt(2).Status);
        Assert.Throws<DomainException>(() => wf.Approve("owner@x", T0)); // nothing left to decide
    }

    [Fact]
    public void Delegate_lets_the_delegate_decide_and_resets_sla()
    {
        var wf = StartWorkflow();
        wf.Approve("agent@x", T0);

        wf.Delegate("legal@x", "paralegal@x", T0.AddHours(10));

        var step = wf.CurrentStep!;
        Assert.Equal("paralegal@x", step.DelegatedTo);
        Assert.Equal(T0.AddHours(34), step.DueUtc);
        Assert.True(step.CanActBy("paralegal@x"));
        Assert.True(step.CanActBy("legal@x"));

        wf.Approve("paralegal@x", T0.AddHours(11));
        Assert.Equal(ApprovalRole.Owner, wf.CurrentStep!.Role);
        Assert.Equal("paralegal@x", step.DecidedBy);
    }

    [Fact]
    public void Delegate_rejects_self_and_non_assignees()
    {
        var wf = StartWorkflow();
        Assert.Throws<DomainException>(() => wf.Delegate("agent@x", "agent@x", T0));
        Assert.Throws<NotPermittedException>(() => wf.Delegate("legal@x", "someone@x", T0));
    }

    [Fact]
    public void Escalation_moves_overdue_step_up_the_chain_then_to_fallback_owner()
    {
        var wf = StartWorkflow();

        Assert.Null(wf.EscalateIfOverdue(T0.AddHours(23))); // within SLA

        var first = wf.EscalateIfOverdue(T0.AddHours(25));
        Assert.NotNull(first);
        Assert.Equal("legal@x", first!.EscalatedTo);
        Assert.Equal(1, first.EscalationCount);
        Assert.Equal(ApprovalRole.Agent, first.Role); // step keeps its role
        Assert.Equal(T0.AddHours(49), first.DueUtc);
        Assert.True(first.CanActBy("legal@x"));

        var second = wf.EscalateIfOverdue(T0.AddHours(50));
        Assert.Equal("owner@x", second!.EscalatedTo);

        var third = wf.EscalateIfOverdue(T0.AddHours(75));
        Assert.Equal("admin@x", third!.EscalatedTo);

        Assert.Null(wf.EscalateIfOverdue(T0.AddHours(100))); // nobody left; manual intervention
        Assert.Equal(3, wf.CurrentStep!.EscalationCount);
    }

    [Fact]
    public void Escalated_approver_can_approve_on_behalf_of_original_assignee()
    {
        var wf = StartWorkflow();
        wf.EscalateIfOverdue(T0.AddHours(30));

        wf.Approve("legal@x", T0.AddHours(31));

        Assert.Equal(StepStatus.Approved, wf.OrderedSteps.First().Status);
        Assert.Equal("legal@x", wf.OrderedSteps.First().DecidedBy);
        Assert.Equal(ApprovalRole.Legal, wf.CurrentStep!.Role);
    }

    [Fact]
    public void Cancel_skips_open_steps_and_returns_document_to_draft()
    {
        var doc = TestData.NewDocument();
        var wf = StartWorkflow(doc);
        wf.Approve("agent@x", T0);

        wf.Cancel("agent@x", T0.AddHours(1));

        Assert.Equal(WorkflowStatus.Cancelled, wf.Status);
        Assert.Equal(DocumentStatus.Draft, doc.Status);
        Assert.Equal(StepStatus.Approved, wf.OrderedSteps.ElementAt(0).Status);
        Assert.Equal(StepStatus.Skipped, wf.OrderedSteps.ElementAt(1).Status);
        Assert.Throws<DomainException>(() => wf.Cancel("agent@x", T0));
    }

    [Fact]
    public void Assignments_must_name_every_role()
    {
        var incomplete = new ApprovalChainAssignments("agent@x", "", "owner@x");
        Assert.Throws<DomainException>(() => ApprovalWorkflow.Start(TestData.NewDocument(), "agent@x", incomplete, T0));
    }

    [Fact]
    public void Next_role_follows_agent_legal_owner()
    {
        Assert.Equal(ApprovalRole.Legal, ApprovalChain.NextRole(ApprovalRole.Agent));
        Assert.Equal(ApprovalRole.Owner, ApprovalChain.NextRole(ApprovalRole.Legal));
        Assert.Null(ApprovalChain.NextRole(ApprovalRole.Owner));
    }
}
