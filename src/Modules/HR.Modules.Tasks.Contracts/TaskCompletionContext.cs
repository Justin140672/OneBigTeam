namespace HR.Modules.Tasks.Contracts;

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
    string? OutcomeReason = null,
    // Ticket 11 (P1): the durable TaskCompletionOperation.Id behind this dispatch — a stable
    // identity an action can use to derive its own idempotency key (e.g. for a downstream
    // ITaskCreator.CreateAsync call, or a module-owned SaveIdempotentAsync record) so a resumed/
    // retried dispatch for the SAME operation never repeats a business mutation or side effect,
    // while a genuinely new completion attempt (a fresh operation, e.g. after a Rejected one) gets
    // a fresh identity. Guid.Empty for any caller that hasn't been updated to supply one yet.
    Guid DispatchOperationId = default);
