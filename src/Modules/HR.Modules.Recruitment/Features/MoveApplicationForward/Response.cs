namespace HR.Modules.Recruitment.Features.MoveApplicationForward;

internal sealed record MoveApplicationForwardResponse(
    Guid Id,
    Guid VacancyId,
    Guid CandidateId,
    Guid PreviousStageId,
    Guid CurrentStageId,
    string CurrentStageName,
    string? CvReviewNotes,
    DateTimeOffset UpdatedAt);
