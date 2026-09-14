namespace HR.Modules.Recruitment.Features.PublishVacancy;

internal sealed record PublishVacancyRequest
{
    public Guid CompanyId { get; init; }
    public Guid VacancyId { get; init; }
    public DateOnly? OpenedAt { get; init; }

    // Populated by the endpoint from the optional "Idempotency-Key" request header (ticket 3, P1
    // follow-up). Null when the caller didn't supply one, in which case no dedup is attempted.
    internal string? IdempotencyKey { get; init; }
}
