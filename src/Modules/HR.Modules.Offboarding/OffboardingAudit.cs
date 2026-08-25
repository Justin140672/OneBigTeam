using HR.SharedKernel;

namespace HR.Modules.Offboarding;

// OFF-08: plan creation/start — the counterpart to OffboardingPlanCompletedAuditEvent, giving HR a
// documented starting point for the audit trail. ActorEmployeeId is the HR user who triggered
// "Start Offboarding" from the UI (human path, via StartOffboarding's Endpoint/Request) or
// OffboardingSystemActor.Id when the plan was auto-created as a side effect of Employees'
// StartLeavingProcess (system path, via IOffboardingPlanCoordinator.StartAsync — see
// OffboardingPlanCoordinator).
internal sealed record OffboardingPlanStartedAuditEvent(
    Guid CompanyId,
    Guid OffboardingPlanId,
    Guid EmployeeId,
    Guid ActorEmployeeId,
    DateOnly LastWorkingDay,
    int TotalTasks,
    int MandatoryTasks,
    bool IsBackdated,
    DateTimeOffset OccurredAt) : IAuditEvent
{
    string IAuditEvent.EventType        => "offboarding-plan.started";
    string IAuditEvent.EntityType       => "OffboardingPlan";
    Guid   IAuditEvent.EntityId         => OffboardingPlanId;
    Guid?  IAuditEvent.EmployeeId       => EmployeeId;
    Guid?  IAuditEvent.ActorUserId      => ActorEmployeeId == OffboardingSystemActor.Id ? null : ActorEmployeeId;
    Guid?  IAuditEvent.ActorEmployeeId  => ActorEmployeeId;
    Guid?  IAuditEvent.CorrelationId    => null;
    string? IAuditEvent.Summary         => $"Offboarding plan started ({TotalTasks} task(s), {MandatoryTasks} mandatory)";
    object? IAuditEvent.Before          => null;
    object? IAuditEvent.After           => new { Status = "InProgress", LastWorkingDay, TotalTasks, MandatoryTasks, IsBackdated };
    object? IAuditEvent.Metadata        => null;
}

// Published when a plan completes — the moment the Offboarding tab on the Employee Overview page
// stops being shown (see EmployeeEdit.razor's _showOffboardingTab). This is what lets HR still
// find completed offboarding history in the Audit tab afterward; nothing about the underlying
// OffboardingPlan/OffboardingTask rows is deleted, only the tab disappears.
//
// OFF-08: ActorEmployeeId is the person whose task completion/skip resolved the plan's last
// outstanding mandatory task (from TaskCompletionContext.CompletedBy, resolved server-side by
// CompleteTaskHandler — never client-supplied), or OffboardingSystemActor.Id if that actor could
// not be resolved (e.g. a legacy/waived task with no recorded actor tail).
internal sealed record OffboardingPlanCompletedAuditEvent(
    Guid CompanyId,
    Guid OffboardingPlanId,
    Guid EmployeeId,
    Guid ActorEmployeeId,
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
    Guid?  IAuditEvent.ActorUserId      => ActorEmployeeId == OffboardingSystemActor.Id ? null : ActorEmployeeId;
    Guid?  IAuditEvent.ActorEmployeeId  => ActorEmployeeId;
    Guid?  IAuditEvent.CorrelationId    => null;
    string? IAuditEvent.Summary         => $"Offboarding completed ({CompletedTasks} of {TotalTasks} tasks done, {SkippedTasks} skipped)";
    object? IAuditEvent.Before          => new { Status = "InProgress" };
    object? IAuditEvent.After           => new { Status = "Completed", LastWorkingDay, TotalTasks, CompletedTasks, SkippedTasks };
    object? IAuditEvent.Metadata        => null;
}

// OFF-08: published for every individual OffboardingTask completion — the task-level counterpart
// to OffboardingPlanCompletedAuditEvent, giving HR a per-task trace (not just the plan-level
// roll-up) including which Tasks-module/Assets-module entity the task was linked to. Actor is
// TaskCompletionContext.CompletedBy, resolved server-side — never client-supplied.
internal sealed record OffboardingTaskCompletedAuditEvent(
    Guid CompanyId,
    Guid OffboardingPlanId,
    Guid OffboardingTaskId,
    Guid TaskItemId,
    Guid EmployeeId,
    Guid ActorEmployeeId,
    string Title,
    Guid? AssetAssignmentId,
    DateTimeOffset OccurredAt) : IAuditEvent
{
    string IAuditEvent.EventType        => "offboarding-task.completed";
    string IAuditEvent.EntityType       => "OffboardingTask";
    Guid   IAuditEvent.EntityId         => OffboardingTaskId;
    Guid?  IAuditEvent.EmployeeId       => EmployeeId;
    Guid?  IAuditEvent.ActorUserId      => ActorEmployeeId == OffboardingSystemActor.Id ? null : ActorEmployeeId;
    Guid?  IAuditEvent.ActorEmployeeId  => ActorEmployeeId;
    Guid?  IAuditEvent.CorrelationId    => OffboardingPlanId;
    string? IAuditEvent.Summary         => $"Offboarding task completed: {Title}";
    object? IAuditEvent.Before          => new { Status = "Pending" };
    object? IAuditEvent.After           => new { Status = "Completed", TaskItemId, AssetAssignmentId };
    object? IAuditEvent.Metadata        => null;
}

// OFF-08: task-level skip, mirroring OffboardingTaskCompletedAuditEvent. SkipReason is included
// verbatim — offboarding task skip reasons are operational (e.g. "asset already returned via
// Assets module", "employee's leaving process was withdrawn", HR-entered handover notes), never
// health/financial/personal-sensitive content, so this does not need the same free-text redaction
// SicknessAudit applies to clinical notes. Actor is Skip()'s actorUserId parameter (either a real
// resolved user for a human skip, or OffboardingSystemActor.Id for a system-initiated skip such as
// leaving-process cancellation cascades or CreateWaived).
internal sealed record OffboardingTaskSkippedAuditEvent(
    Guid CompanyId,
    Guid OffboardingPlanId,
    Guid OffboardingTaskId,
    Guid? TaskItemId,
    Guid EmployeeId,
    Guid ActorEmployeeId,
    string Title,
    string SkipReason,
    Guid? AssetAssignmentId,
    DateTimeOffset OccurredAt) : IAuditEvent
{
    string IAuditEvent.EventType        => "offboarding-task.skipped";
    string IAuditEvent.EntityType       => "OffboardingTask";
    Guid   IAuditEvent.EntityId         => OffboardingTaskId;
    Guid?  IAuditEvent.EmployeeId       => EmployeeId;
    Guid?  IAuditEvent.ActorUserId      => ActorEmployeeId == OffboardingSystemActor.Id ? null : ActorEmployeeId;
    Guid?  IAuditEvent.ActorEmployeeId  => ActorEmployeeId;
    Guid?  IAuditEvent.CorrelationId    => OffboardingPlanId;
    string? IAuditEvent.Summary         => $"Offboarding task skipped: {Title} — {SkipReason}";
    object? IAuditEvent.Before          => new { Status = "Pending" };
    object? IAuditEvent.After           => new { Status = "Skipped", TaskItemId, SkipReason, AssetAssignmentId };
    object? IAuditEvent.Metadata        => null;
}

// Published when an offboarding plan is cancelled as a side effect of the employee's Leaving
// Process being cancelled (see IOffboardingPlanCoordinator.CancelOutstandingTasksAsync). Like
// OffboardingPlanCompletedAuditEvent above, this is what lets HR still find the cancelled
// offboarding history in the Audit tab afterward — no OffboardingPlan/OffboardingTask rows are
// deleted, only their statuses change.
//
// OFF-08: ActorEmployeeId is always OffboardingSystemActor.Id — this is a cascade side effect of
// leaving-process cancellation, not a direct offboarding action; the human who actually cancelled
// the leaving process is already attributed on the originating EmployeesAudit event.
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
    Guid?  IAuditEvent.ActorEmployeeId  => OffboardingSystemActor.Id;
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
// OFF-08: ActorEmployeeId is always OffboardingSystemActor.Id — same reasoning as
// OffboardingPlanCancelledAuditEvent above; the human who amended the leaving date is already
// attributed on the originating EmployeesAudit event for that action.
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
    Guid?  IAuditEvent.ActorEmployeeId  => OffboardingSystemActor.Id;
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
// OFF-08: ActorEmployeeId is always OffboardingSystemActor.Id — raised automatically by
// EmployeeDepartureFinalizer's finalisation flow, never a direct HR action.
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
    Guid?  IAuditEvent.ActorEmployeeId  => OffboardingSystemActor.Id;
    Guid?  IAuditEvent.CorrelationId    => null;
    string? IAuditEvent.Summary         => $"Employee departed with {OutstandingMandatoryTasks} outstanding mandatory offboarding task(s) — HR exception raised";
    object? IAuditEvent.Before          => new { HasIncompleteOffboardingAtDeparture = false };
    object? IAuditEvent.After           => new { HasIncompleteOffboardingAtDeparture = true, OutstandingMandatoryTasks };
    object? IAuditEvent.Metadata        => null;
}
