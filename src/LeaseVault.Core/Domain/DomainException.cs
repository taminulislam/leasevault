namespace LeaseVault.Core.Domain;

/// <summary>
/// Raised when a business rule is violated. Callers map this to a 4xx result / validation message.
/// </summary>
public class DomainException : Exception
{
    public DomainException(string message) : base(message)
    {
    }
}

/// <summary>
/// Raised when the current user is not permitted to perform a domain action.
/// </summary>
public sealed class NotPermittedException : DomainException
{
    public NotPermittedException(string message) : base(message)
    {
    }
}

/// <summary>
/// Raised when a document is locked by another user (check-out).
/// </summary>
public sealed class DocumentLockedException : DomainException
{
    public string LockOwner { get; }

    public DocumentLockedException(string lockOwner)
        : base($"Document is checked out by '{lockOwner}'.")
    {
        LockOwner = lockOwner;
    }
}
