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
    Guid? ReplacementManagerEmployeeId = null);
