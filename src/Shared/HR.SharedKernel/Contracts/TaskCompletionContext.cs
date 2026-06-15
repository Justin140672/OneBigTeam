namespace HR.SharedKernel.Contracts;

/// <summary>
/// Data passed to every <see cref="ITaskCompletionAction"/> when a task transitions to Completed.
/// </summary>
public sealed record TaskCompletionContext(
    Guid CompanyId,
    Guid TaskId,
    string Title,
    string? Description,
    TaskSource Source,
    Guid? AssignedEmployeeId,
    Guid CompletedBy,
    DateTimeOffset CompletedAt);
