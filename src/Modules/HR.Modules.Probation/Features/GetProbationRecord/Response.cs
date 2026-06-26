namespace HR.Modules.Probation.Features.GetProbationRecord;

internal sealed record GetProbationRecordResponse(
    Guid Id,
    Guid CompanyId,
    string Status,
    DateOnly ExpectedEndDate,
    string? ExtensionReason,
    DateOnly? DecisionDate,
    Guid? DecisionMakerEmployeeId,
    string? OutcomeNotes);
