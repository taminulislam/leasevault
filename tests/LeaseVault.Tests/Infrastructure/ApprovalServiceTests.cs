using LeaseVault.Core.Abstractions;
using LeaseVault.Core.Domain;
using LeaseVault.Infrastructure.Approvals;
using LeaseVault.Infrastructure.Persistence;
using LeaseVault.Infrastructure.Services;
using LeaseVault.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace LeaseVault.Tests.Infrastructure;

public class ApprovalServiceTests : IDisposable
{
    private readonly SqliteTestDatabase _database = new();
    private readonly FakeClock _clock = new(TestData.Now);
    private readonly RecordingEmailSender _email = new();
    private readonly ApprovalOptions _options = new() { StepSlaHours = 24, FallbackOwner = "admin@leasevault.local" };

    public void Dispose() => _database.Dispose();

    private (LeaseVaultDbContext Db, ApprovalService Service) Build(string user)
    {
        var db = _database.CreateContext();
        var currentUser = new TestUser(user, "Agents");
        var service = new ApprovalService(
            db,
            new ConfiguredApproverDirectory(Options.Create(_options)),
            _email,
            new EfAuditLog(db, currentUser, _clock),
            currentUser,
            _clock,
            Options.Create(_options),
            TestData.Logger<ApprovalService>());
        return (db, service);
    }

    private async Task<int> SeedDocumentAsync()
    {
        await using var db = _database.CreateContext();
        var doc = TestData.NewDocument(id: 0);
        db.Documents.Add(doc);
        await db.SaveChangesAsync();
        return doc.Id;
    }

    [Fact]
    public async Task Start_persists_chain_notifies_first_approver_and_audits()
    {
        var docId = await SeedDocumentAsync();
        var (db, service) = Build("agent@leasevault.local");

        var wf = await service.StartAsync(docId);

        Assert.Equal(3, wf.Steps.Count);
        Assert.Equal(_options.DefaultAgent, Assert.Single(_email.Sent).To);
        Assert.Contains(await db.AuditEntries.ToListAsync(), a => a.Action == AuditActions.WorkflowStarted);
        Assert.Equal(DocumentStatus.PendingApproval, (await db.Documents.SingleAsync()).Status);
    }

    [Fact]
    public async Task Overdue_steps_are_escalated_to_next_approver_with_notification_and_audit()
    {
        var docId = await SeedDocumentAsync();
        var (_, starter) = Build("agent@leasevault.local");
        var wf = await starter.StartAsync(docId);
        _email.Sent.Clear();

        var (_, sweeper) = Build("system");
        Assert.Equal(0, await sweeper.EscalateOverdueAsync()); // not overdue yet

        _clock.Advance(TimeSpan.FromHours(25));
        Assert.Equal(1, await sweeper.EscalateOverdueAsync());

        await using var verify = _database.CreateContext();
        var step = await verify.ApprovalSteps.SingleAsync(s => s.WorkflowId == wf.Id && s.Status == StepStatus.Pending);
        Assert.Equal(_options.DefaultLegal, step.EscalatedTo);
        Assert.Equal(1, step.EscalationCount);
        Assert.Equal(_clock.UtcNow.AddHours(24), step.DueUtc);

        var mail = Assert.Single(_email.Sent);
        Assert.Equal(_options.DefaultLegal, mail.To);
        Assert.Contains("Escalated", mail.Subject);
        Assert.Contains(await verify.AuditEntries.ToListAsync(), a => a.Action == AuditActions.StepEscalated && a.Actor == "system");

        Assert.Equal(0, await sweeper.EscalateOverdueAsync()); // fresh SLA window
    }

    [Fact]
    public async Task Full_chain_approve_reject_and_delegate_through_service()
    {
        var docId = await SeedDocumentAsync();
        var (_, agent) = Build("agent@leasevault.local");
        var wf = await agent.StartAsync(docId);

        await agent.ApproveAsync(wf.Id, "ok");

        var (_, legal) = Build("legal@leasevault.local");
        await legal.DelegateAsync(wf.Id, "paralegal@leasevault.local");

        var (_, paralegal) = Build("paralegal@leasevault.local");
        await Assert.ThrowsAsync<DomainException>(() => paralegal.RejectAsync(wf.Id, ""));
        await paralegal.RejectAsync(wf.Id, "Missing exhibit B");

        await using var verify = _database.CreateContext();
        var persisted = await verify.ApprovalWorkflows.Include(w => w.Steps).Include(w => w.Document).SingleAsync();
        Assert.Equal(WorkflowStatus.Rejected, persisted.Status);
        Assert.Equal(DocumentStatus.Rejected, persisted.Document!.Status);
        Assert.Equal("paralegal@leasevault.local", persisted.Steps.Single(s => s.Role == ApprovalRole.Legal).DecidedBy);
        Assert.Contains(_email.Sent, m => m.Subject.Contains("rejected") && m.To == "agent@leasevault.local");
    }
}
