namespace HR.Modules.Probation.Features.CreateProbationRecord;

internal sealed record CreateProbationRecordResponse(
    Guid Id,
    Guid CompanyId,
    Guid EmployeeId,
    Guid ManagerEmployeeId,
    DateOnly StartDate,
    DateOnly ExpectedEndDate,
    string Status,
    string? Notes,
    DateTimeOffset CreatedAt);
