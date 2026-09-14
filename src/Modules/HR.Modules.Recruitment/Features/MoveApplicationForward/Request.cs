namespace HR.Modules.Recruitment.Features.MoveApplicationForward;

internal sealed record MoveApplicationForwardRequest
{
    public Guid CompanyId { get; init; }
    public Guid VacancyId { get; init; }
    public Guid ApplicationId { get; init; }

    // Optional: CV review notes to persist against the application before advancing the stage
    // (the "Move Forward" action on the Review CV screen saves notes and progresses in one step).
    public string? CvReviewNotes { get; init; }

    // Populated by the endpoint from the optional "Idempotency-Key" request header (ticket 3, P1
    // follow-up). Null when the caller didn't supply one, in which case no dedup is attempted.
    internal string? IdempotencyKey { get; init; }
}
