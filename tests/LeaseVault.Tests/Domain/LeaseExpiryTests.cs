using LeaseVault.Core.Domain;

namespace LeaseVault.Tests.Domain;

public class LeaseExpiryTests
{
    private static readonly DateOnly Today = new(2026, 3, 1);

    private static Lease ActiveLeaseEndingIn(int days, LeaseStatus status = LeaseStatus.Active) => new()
    {
        StartDate = Today.AddYears(-1),
        EndDate = Today.AddDays(days),
        Status = status
    };

    [Theory]
    [InlineData(0, true)]
    [InlineData(30, true)]
    [InlineData(60, true)]
    [InlineData(61, false)]
    [InlineData(-1, false)]
    public void Reminder_is_due_only_inside_lead_window(int daysUntilEnd, bool expected)
    {
        var lease = ActiveLeaseEndingIn(daysUntilEnd);
        Assert.Equal(expected, lease.NeedsExpiryReminder(Today, leadDays: 60));
    }

    [Theory]
    [InlineData(LeaseStatus.Draft)]
    [InlineData(LeaseStatus.Expired)]
    [InlineData(LeaseStatus.Terminated)]
    [InlineData(LeaseStatus.Renewed)]
    public void Reminder_not_sent_for_inactive_leases(LeaseStatus status)
    {
        var lease = ActiveLeaseEndingIn(10, status);
        Assert.False(lease.NeedsExpiryReminder(Today, 60));
    }

    [Fact]
    public void Reminder_is_sent_once_and_marks_lease_expiring()
    {
        var lease = ActiveLeaseEndingIn(20);

        lease.MarkReminderSent(new DateTime(2026, 3, 1, 8, 0, 0, DateTimeKind.Utc));

        Assert.Equal(LeaseStatus.Expiring, lease.Status);
        Assert.False(lease.NeedsExpiryReminder(Today, 60));
    }

    [Fact]
    public void Activate_requires_draft_and_valid_dates()
    {
        var lease = new Lease { StartDate = Today, EndDate = Today, Status = LeaseStatus.Draft };
        Assert.Throws<DomainException>(() => lease.Activate());

        lease.EndDate = Today.AddYears(1);
        lease.Activate();
        Assert.Equal(LeaseStatus.Active, lease.Status);
        Assert.Throws<DomainException>(() => lease.Activate());
    }

    [Fact]
    public void Terminate_is_not_allowed_twice()
    {
        var lease = ActiveLeaseEndingIn(100);
        lease.Terminate();
        Assert.Equal(LeaseStatus.Terminated, lease.Status);
        Assert.Throws<DomainException>(() => lease.Terminate());
    }
}
