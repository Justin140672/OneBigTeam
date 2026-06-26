namespace HR.Modules.Probation.Features.GetProbationRecordByEmployee;

internal sealed record GetProbationRecordByEmployeeResponse(
    Guid Id,
    Guid CompanyId,
    Guid EmployeeId,
    Guid ManagerEmployeeId,
    DateOnly StartDate,
    DateOnly ExpectedEndDate,
    string Status,
    string? Notes,
    string? ExtensionReason,
    DateOnly? DecisionDate,
    Guid? DecisionMakerEmployeeId,
    string? OutcomeNotes,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
