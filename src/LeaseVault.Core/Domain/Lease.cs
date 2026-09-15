using System.ComponentModel.DataAnnotations;

namespace LeaseVault.Core.Domain;

public class Lease
{
    public int Id { get; set; }

    public int UnitId { get; set; }
    public Unit? Unit { get; set; }

    public int TenantId { get; set; }
    public Tenant? Tenant { get; set; }

    [DataType(DataType.Date)]
    public DateOnly StartDate { get; set; }

    [DataType(DataType.Date)]
    public DateOnly EndDate { get; set; }

    [Range(0, 10_000_000)]
    public decimal MonthlyRent { get; set; }

    [Range(0, 10_000_000)]
    public decimal SecurityDeposit { get; set; }

    public LeaseStatus Status { get; set; } = LeaseStatus.Draft;

    /// <summary>When the expiry reminder was last sent; null if never.</summary>
    public DateTime? ExpiryReminderSentUtc { get; set; }

    [MaxLength(500)]
    public string? Notes { get; set; }

    public ICollection<Document> Documents { get; set; } = new List<Document>();

    public int DaysUntilExpiry(DateOnly today) => EndDate.DayNumber - today.DayNumber;

    /// <summary>
    /// A lease needs an expiry reminder when it is still in force, ends within the lead window
    /// and no reminder has been sent yet.
    /// </summary>
    public bool NeedsExpiryReminder(DateOnly today, int leadDays)
    {
        if (leadDays < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(leadDays));
        }

        if (Status is not (LeaseStatus.Active or LeaseStatus.Expiring))
        {
            return false;
        }

        if (ExpiryReminderSentUtc is not null)
        {
            return false;
        }

        var days = DaysUntilExpiry(today);
        return days >= 0 && days <= leadDays;
    }

    public void MarkReminderSent(DateTime nowUtc)
    {
        ExpiryReminderSentUtc = nowUtc;
        if (Status == LeaseStatus.Active)
        {
            Status = LeaseStatus.Expiring;
        }
    }

    public void Activate()
    {
        if (Status != LeaseStatus.Draft)
        {
            throw new DomainException($"Only draft leases can be activated (current status {Status}).");
        }

        if (EndDate <= StartDate)
        {
            throw new DomainException("Lease end date must be after the start date.");
        }

        Status = LeaseStatus.Active;
    }

    public void Terminate()
    {
        if (Status is LeaseStatus.Terminated or LeaseStatus.Expired)
        {
            throw new DomainException($"Lease is already {Status}.");
        }

        Status = LeaseStatus.Terminated;
    }
}
