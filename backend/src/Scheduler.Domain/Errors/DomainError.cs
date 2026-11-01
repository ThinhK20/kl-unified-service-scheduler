namespace Scheduler.Domain.Errors;

/// <summary>A broken domain rule: a stable code plus a plain message.</summary>
public sealed record DomainError(string Code, string Message);
