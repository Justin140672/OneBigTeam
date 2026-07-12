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
    DateTimeOffset OccurredAt) : IAuditEvent
{
    string IAuditEvent.EventType        => "onboarding-plan.completed";
    string IAuditEvent.EntityType       => "OnboardingPlan";
    Guid   IAuditEvent.EntityId         => OnboardingPlanId;
    Guid?  IAuditEvent.EmployeeId       => EmployeeId;
    Guid?  IAuditEvent.ActorUserId      => null;
    Guid?  IAuditEvent.ActorEmployeeId  => null;
    Guid?  IAuditEvent.CorrelationId    => null;
    string? IAuditEvent.Summary         => $"Onboarding completed ({CompletedTasks} of {TotalTasks} tasks done, {SkippedTasks} skipped)";
    object? IAuditEvent.Before          => new { Status = "InProgress" };
    object? IAuditEvent.After           => new { Status = "Completed", StartDate, TotalTasks, CompletedTasks, SkippedTasks };
    object? IAuditEvent.Metadata        => null;
}
