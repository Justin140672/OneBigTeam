namespace HR.Modules.Recruitment.Features.SaveCvReviewNotes;

internal sealed record SaveCvReviewNotesResponse(
    Guid Id,
    Guid VacancyId,
    Guid CandidateId,
    Guid CurrentStageId,
    string? CvReviewNotes,
    DateTimeOffset? CvReviewedAt,
    Guid? CvReviewedByUserId,
    DateTimeOffset UpdatedAt);
