namespace HR.Modules.Recruitment.Features.MoveApplicationForward;

internal sealed record MoveApplicationForwardRequest
{
    public Guid CompanyId { get; init; }
    public Guid VacancyId { get; init; }
    public Guid ApplicationId { get; init; }

    // Optional: CV review notes to persist against the application before advancing the stage
    // (the "Move Forward" action on the Review CV screen saves notes and progresses in one step).
    public string? CvReviewNotes { get; init; }
}
