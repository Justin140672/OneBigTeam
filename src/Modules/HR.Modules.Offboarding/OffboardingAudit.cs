using HR.SharedKernel;

namespace HR.Modules.Offboarding;

// Published when a plan completes — the moment the Offboarding tab on the Employee Overview page
// stops being shown (see EmployeeEdit.razor's _showOffboardingTab). This is what lets HR still
// find completed offboarding history in the Audit tab afterward; nothing about the underlying
// OffboardingPlan/OffboardingTask rows is deleted, only the tab disappears.
internal sealed record OffboardingPlanCompletedAuditEvent(
    Guid CompanyId,
    Guid OffboardingPlanId,
    Guid EmployeeId,
    DateOnly LastWorkingDay,
    int TotalTasks,
    int CompletedTasks,
    int SkippedTasks,
    DateTimeOffset OccurredAt) : IAuditEvent
{
    string IAuditEvent.EventType        => "offboarding-plan.completed";
    string IAuditEvent.EntityType       => "OffboardingPlan";
    Guid   IAuditEvent.EntityId         => OffboardingPlanId;
    Guid?  IAuditEvent.EmployeeId       => EmployeeId;
    Guid?  IAuditEvent.ActorUserId      => null;
    Guid?  IAuditEvent.ActorEmployeeId  => null;
    Guid?  IAuditEvent.CorrelationId    => null;
    string? IAuditEvent.Summary         => $"Offboarding completed ({CompletedTasks} of {TotalTasks} tasks done, {SkippedTasks} skipped)";
    object? IAuditEvent.Before          => new { Status = "InProgress" };
    object? IAuditEvent.After           => new { Status = "Completed", LastWorkingDay, TotalTasks, CompletedTasks, SkippedTasks };
    object? IAuditEvent.Metadata        => null;
}

// Published when an offboarding plan is cancelled as a side effect of the employee's Leaving
// Process being cancelled (see IOffboardingPlanCoordinator.CancelOutstandingTasksAsync). Like
// OffboardingPlanCompletedAuditEvent above, this is what lets HR still find the cancelled
// offboarding history in the Audit tab afterward — no OffboardingPlan/OffboardingTask rows are
// deleted, only their statuses change.
internal sealed record OffboardingPlanCancelledAuditEvent(
    Guid CompanyId,
    Guid OffboardingPlanId,
    Guid EmployeeId,
    int OutstandingTasksCancelled,
    DateTimeOffset OccurredAt) : IAuditEvent
{
    string IAuditEvent.EventType        => "offboarding-plan.cancelled";
    string IAuditEvent.EntityType       => "OffboardingPlan";
    Guid   IAuditEvent.EntityId         => OffboardingPlanId;
    Guid?  IAuditEvent.EmployeeId       => EmployeeId;
    Guid?  IAuditEvent.ActorUserId      => null;
    Guid?  IAuditEvent.ActorEmployeeId  => null;
    Guid?  IAuditEvent.CorrelationId    => null;
    string? IAuditEvent.Summary         => $"Offboarding cancelled ({OutstandingTasksCancelled} outstanding task(s) cancelled) — employee's leaving process was withdrawn";
    object? IAuditEvent.Before          => new { Status = "InProgress" };
    object? IAuditEvent.After           => new { Status = "Cancelled", OutstandingTasksCancelled };
    object? IAuditEvent.Metadata        => null;
}
