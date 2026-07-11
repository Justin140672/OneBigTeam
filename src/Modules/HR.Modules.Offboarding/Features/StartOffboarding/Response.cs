namespace HR.Modules.Offboarding.Features.StartOffboarding;

internal sealed record StartOffboardingResponse(
    Guid Id,
    Guid CompanyId,
    Guid EmployeeId,
    DateOnly LastWorkingDay,
    string Status,
    string? Notes,
    IReadOnlyList<Guid> GeneratedTaskIds,
    DateTimeOffset CreatedAt);
