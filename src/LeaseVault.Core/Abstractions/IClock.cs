namespace LeaseVault.Core.Abstractions;

/// <summary>Injectable time source so rules that depend on "now" are testable.</summary>
public interface IClock
{
    DateTime UtcNow { get; }

    DateOnly Today => DateOnly.FromDateTime(UtcNow);
}

public sealed class SystemClock : IClock
{
    public DateTime UtcNow => DateTime.UtcNow;
}
