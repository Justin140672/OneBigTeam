namespace HR.Modules.Probation.Features.UpdateProbationRecord;

internal sealed record UpdateProbationRecordResponse(
    Guid Id,
    Guid CompanyId,
    Guid EmployeeId,
    Guid ManagerEmployeeId,
    DateOnly StartDate,
    DateOnly ExpectedEndDate,
    string Status,
    string? Notes,
    string? ExtensionReason,
    Guid? DecisionMakerEmployeeId,
    DateOnly? DecisionDate,
    string? OutcomeNotes,
    DateTimeOffset UpdatedAt);
