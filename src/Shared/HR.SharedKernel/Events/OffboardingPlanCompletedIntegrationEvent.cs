namespace HR.SharedKernel;

// Published when an employee's offboarding plan finishes (all tasks completed/skipped).
// P1 fix: no longer consumed to disable the employee's user account — offboarding checklist
// completion must not control application-account lifecycle (see
// HR.Modules.Employees.Services.EmployeeDepartureFinalizer /
// HR.Modules.Identity.Features.OnEmployeeDepartureFinalised, which is now the sole trigger for
// departure-driven account disablement). Currently has no consumers; left published as a
// general-purpose operational signal for future use.
public sealed record OffboardingPlanCompletedIntegrationEvent(
    Guid CompanyId,
    Guid EmployeeId,
    Guid OffboardingPlanId,
    DateTimeOffset OccurredAt) : IIntegrationEvent;
