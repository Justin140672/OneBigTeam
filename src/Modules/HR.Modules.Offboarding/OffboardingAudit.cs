using HR.SharedKernel;

namespace HR.Modules.Offboarding;

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
    // Ticket 15 (P1): deterministic EventId derived from the plan id — exactly one
    // OffboardingPlanCompletedAuditEvent is ever meaningful per plan, so reusing OffboardingPlanId
    // as the idempotency key means a recovered/replayed dispatch (see
    // CompleteOffboardingTaskFromTaskAction.RecoverPlanCompletionEffectsAsync) can never create a
    // duplicate audit row: DbAuditEventPublisher dedupes on EventId via a unique index.
    Guid IAuditEvent.EventId            => OffboardingPlanId;
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

internal sealed record OffboardingTaskWaivedAuditEvent(
    Guid CompanyId,
    Guid OffboardingPlanId,
    Guid OffboardingTaskId,
    Guid EmployeeId,
    Guid ActorEmployeeId,
    string Title,
    string Reason,
    DateTimeOffset OccurredAt) : IAuditEvent
{
    string IAuditEvent.EventType        => "offboarding-task.waived";
    string IAuditEvent.EntityType       => "OffboardingTask";
    Guid   IAuditEvent.EntityId         => OffboardingTaskId;
    Guid?  IAuditEvent.EmployeeId       => EmployeeId;
    Guid?  IAuditEvent.ActorUserId      => ActorEmployeeId == OffboardingSystemActor.Id ? null : ActorEmployeeId;
    Guid?  IAuditEvent.ActorEmployeeId  => ActorEmployeeId;
    Guid?  IAuditEvent.CorrelationId    => OffboardingPlanId;
    string? IAuditEvent.Summary         => $"Offboarding task waived: {Title} — {Reason}";
    object? IAuditEvent.Before          => new { Status = "Pending" };
    object? IAuditEvent.After           => new { Status = "Waived", Reason };
    object? IAuditEvent.Metadata        => null;
}

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

internal sealed record OffboardingManagerTasksUnassignedAuditEvent(
    Guid CompanyId,
    Guid OffboardingPlanId,
    Guid EmployeeId,
    Guid? FallbackAssigneeEmployeeId,
    int TasksReassigned,
    DateTimeOffset OccurredAt) : IAuditEvent
{
    string IAuditEvent.EventType        => "offboarding-plan.manager-tasks-unassigned";
    string IAuditEvent.EntityType       => "OffboardingPlan";
    Guid   IAuditEvent.EntityId         => OffboardingPlanId;
    Guid?  IAuditEvent.EmployeeId       => EmployeeId;
    Guid?  IAuditEvent.ActorUserId      => null;
    Guid?  IAuditEvent.ActorEmployeeId  => OffboardingSystemActor.Id;
    Guid?  IAuditEvent.CorrelationId    => null;
    string? IAuditEvent.Summary         => $"Employee has no manager; {TasksReassigned} manager-assigned offboarding task(s) moved to {(FallbackAssigneeEmployeeId is null ? "the unassigned HR queue" : "an HR administrator")}";
    object? IAuditEvent.Before          => null;
    object? IAuditEvent.After           => new { FallbackAssigneeEmployeeId, TasksReassigned, RequiresHrReconciliation = true };
    object? IAuditEvent.Metadata        => null;
}

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
