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

// OFF-02: published when the active offboarding plan's LastWorkingDay (and its outstanding task
// due dates) is reconciled after HR amends the employee's leaving date/last working day
// (EmployeeLeavingDateSetIntegrationEvent). Only published when the plan's LastWorkingDay actually
// changes — a replayed amendment carrying the same date is a no-op and produces no audit entry
// (see IOffboardingPlanCoordinator.RescheduleOutstandingTasksAsync).
internal sealed record OffboardingPlanRescheduledAuditEvent(
    Guid CompanyId,
    Guid OffboardingPlanId,
    Guid EmployeeId,
    DateOnly BeforeLastWorkingDay,
    DateOnly AfterLastWorkingDay,
    int OutstandingTasksRescheduled,
    DateTimeOffset OccurredAt) : IAuditEvent
{
    string IAuditEvent.EventType        => "offboarding-plan.rescheduled";
    string IAuditEvent.EntityType       => "OffboardingPlan";
    Guid   IAuditEvent.EntityId         => OffboardingPlanId;
    Guid?  IAuditEvent.EmployeeId       => EmployeeId;
    Guid?  IAuditEvent.ActorUserId      => null;
    Guid?  IAuditEvent.ActorEmployeeId  => null;
    Guid?  IAuditEvent.CorrelationId    => null;
    string? IAuditEvent.Summary         => $"Offboarding last working day changed from {BeforeLastWorkingDay:d} to {AfterLastWorkingDay:d} ({OutstandingTasksRescheduled} outstanding task(s) rescheduled)";
    object? IAuditEvent.Before          => new { LastWorkingDay = BeforeLastWorkingDay };
    object? IAuditEvent.After           => new { LastWorkingDay = AfterLastWorkingDay, OutstandingTasksRescheduled };
    object? IAuditEvent.Metadata        => null;
}

// OFF-07: published when an employee's departure is finalised (Employees' EmployeeDepartureFinalizer)
// while their offboarding plan still had unresolved mandatory tasks — the durable counterpart to
// EmployeeDepartureFinalizer's one-time manager notification, giving HR a permanent audit trail of
// exactly when and for whom offboarding was left incomplete at departure. Published at most once per
// plan (see MarkOffboardingIncompleteOnDepartureFinalisedHandler's wasAlreadyFlagged guard).
internal sealed record OffboardingIncompleteAtDepartureAuditEvent(
    Guid CompanyId,
    Guid OffboardingPlanId,
    Guid EmployeeId,
    int OutstandingMandatoryTasks,
    DateTimeOffset OccurredAt) : IAuditEvent
{
    string IAuditEvent.EventType        => "offboarding-plan.incomplete-at-departure";
    string IAuditEvent.EntityType       => "OffboardingPlan";
    Guid   IAuditEvent.EntityId         => OffboardingPlanId;
    Guid?  IAuditEvent.EmployeeId       => EmployeeId;
    Guid?  IAuditEvent.ActorUserId      => null;
    Guid?  IAuditEvent.ActorEmployeeId  => null;
    Guid?  IAuditEvent.CorrelationId    => null;
    string? IAuditEvent.Summary         => $"Employee departed with {OutstandingMandatoryTasks} outstanding mandatory offboarding task(s) — HR exception raised";
    object? IAuditEvent.Before          => new { HasIncompleteOffboardingAtDeparture = false };
    object? IAuditEvent.After           => new { HasIncompleteOffboardingAtDeparture = true, OutstandingMandatoryTasks };
    object? IAuditEvent.Metadata        => null;
}
