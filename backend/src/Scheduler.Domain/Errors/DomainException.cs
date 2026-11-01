namespace Scheduler.Domain.Errors;

/// <summary>
/// Thrown when code tries to put a domain object into an invalid state.
/// The Application layer checks its rules first, so in normal use this is not thrown.
/// </summary>
public sealed class DomainException : Exception
{
    public DomainException(DomainError error)
        : base(error.Message)
    {
        Error = error;
    }

    /// <summary>The broken rule.</summary>
    public DomainError Error { get; }

    /// <summary>Short cut for <see cref="DomainError.Code"/>.</summary>
    public string Code => Error.Code;
}
