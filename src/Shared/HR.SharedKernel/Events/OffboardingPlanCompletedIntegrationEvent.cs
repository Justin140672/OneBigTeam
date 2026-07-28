namespace HR.SharedKernel;

// Published when an employee's offboarding plan finishes (all tasks completed/skipped). Consumed
// by the Identity module to automatically disable the employee's user account — see
// HR.Modules.Identity.Features.OnOffboardingPlanCompleted.
public sealed record OffboardingPlanCompletedIntegrationEvent(
    Guid CompanyId,
    Guid EmployeeId,
    Guid OffboardingPlanId,
    DateTimeOffset OccurredAt) : IIntegrationEvent;
