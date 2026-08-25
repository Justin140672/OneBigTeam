namespace HR.Modules.Offboarding.Features.StartOffboarding;

internal sealed record StartOffboardingRequest(
    Guid CompanyId,
    Guid EmployeeId,
    DateOnly LastWorkingDay,
    string? Notes,
    // OFF-06: manager HR nominates to take over the departing employee's direct reports (and any
    // of their own pending manager-scoped approvals/reviews), only meaningful when the departing
    // employee actually has direct reports. When omitted for a manager with direct reports, those
    // reports are left without a manager and the case is routed to an HR exception queue.
    Guid? ReplacementManagerEmployeeId = null,
    // OFF-08: who started this plan — populated by the Endpoint from the authenticated user's
    // resolved identity for the manual "Start Offboarding" action (never client-bound), or
    // OffboardingSystemActor.Id by OffboardingPlanCoordinator.StartAsync when the plan is
    // auto-created as a side effect of Employees' StartLeavingProcess.
    Guid? ActorEmployeeId = null);
