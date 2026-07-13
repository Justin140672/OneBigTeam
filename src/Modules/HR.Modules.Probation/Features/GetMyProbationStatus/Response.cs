namespace HR.Modules.Probation.Features.GetMyProbationStatus;

internal sealed record GetMyProbationStatusResponse(
    bool HasRecord,
    Guid? Id,
    DateOnly? StartDate,
    DateOnly? ExpectedEndDate,
    string? Status,
    DateOnly? DecisionDate,
    string? OutcomeNotes);
