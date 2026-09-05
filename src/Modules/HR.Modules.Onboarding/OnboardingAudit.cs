using HR.SharedKernel;

namespace HR.Modules.Onboarding;

// Published when a plan completes — the moment the Onboarding tab on the Employee Overview page
// stops being shown (see EmployeeEdit.razor's _showOnboardingTab). This is what lets HR still
// find completed onboarding history in the Audit tab afterward; nothing about the underlying
// OnboardingPlan/OnboardingTask rows is deleted, only the tab disappears.
internal sealed record OnboardingPlanCompletedAuditEvent(
    Guid CompanyId,
    Guid OnboardingPlanId,
    Guid EmployeeId,
    DateOnly StartDate,
    int TotalTasks,
    int CompletedTasks,
    int SkippedTasks,
    DateTimeOffset OccurredAt,
    // Whoever completed/skipped the specific task that resolved the plan (never the affected
    // employee themselves unless they happened to be the one completing it). Attributed as a
    // Human actor rather than a system actor because plan completion is a direct, immediate
    // consequence of that person's own task-completion action, not an independent background
    // process — mirrors OffboardingPlanCompletedAuditEvent's actorEmployeeId. Without this, the
    // event was previously always rejected by AuditActorAttributionGuard (Human events require a
    // resolvable actor) and silently dropped, so "Onboarding completed" never actually reached
    // the Audit tab despite this record's Summary claiming it would.
    Guid ActorEmployeeId) : IAuditEvent
{
    string IAuditEvent.EventType        => "onboarding-plan.completed";
    string IAuditEvent.EntityType       => "OnboardingPlan";
    Guid   IAuditEvent.EntityId         => OnboardingPlanId;
    Guid?  IAuditEvent.EmployeeId       => EmployeeId;
    Guid?  IAuditEvent.ActorUserId      => null;
    Guid?  IAuditEvent.ActorEmployeeId  => ActorEmployeeId;
    Guid?  IAuditEvent.CorrelationId    => null;
    string? IAuditEvent.Summary         => $"Onboarding completed ({CompletedTasks} of {TotalTasks} tasks done, {SkippedTasks} skipped)";
    object? IAuditEvent.Before          => new { Status = "InProgress" };
    object? IAuditEvent.After           => new { Status = "Completed", StartDate, TotalTasks, CompletedTasks, SkippedTasks };
    object? IAuditEvent.Metadata        => null;
}
