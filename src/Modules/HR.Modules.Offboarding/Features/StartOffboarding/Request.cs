namespace HR.Modules.Offboarding.Features.StartOffboarding;

internal sealed record StartOffboardingRequest(
    Guid CompanyId,
    Guid EmployeeId,
    DateOnly LastWorkingDay,
    string? Notes);
