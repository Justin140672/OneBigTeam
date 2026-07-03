namespace HR.Infrastructure.Abstractions;

/// <summary>
/// Data passed to every <see cref="ITaskCompletionAction"/> when a task transitions to Completed.
/// </summary>
public sealed record TaskCompletionContext(
    Guid CompanyId,
    Guid TaskId,
    string Title,
    string? Description,
    TaskSource Source,
    TaskActionType ActionType,
    Guid? AssignedEmployeeId,
    Guid CompletedBy,
    DateTimeOffset CompletedAt,
    Guid? SourceEntityId = null,
    string? OutcomeDecision = null,
    string? OutcomeReason = null);
