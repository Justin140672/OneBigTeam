namespace HR.Modules.Recruitment.Features.CreateVacancy;

internal sealed record CreateVacancyRequest
{
    public Guid CompanyId { get; init; }
    public Guid PositionProfileId { get; init; }
    public string? AdvertTitle { get; init; }
    public string? AdvertDescription { get; init; }
    public Guid HiringManagerId { get; init; }
    public Guid? AssignedRecruiterId { get; init; }
    public bool IsAdvertisedInternally { get; init; }

    // Populated by the endpoint from the optional "Idempotency-Key" request header (ticket 3, P1
    // follow-up). Null when the caller didn't supply one, in which case no dedup is attempted.
    internal string? IdempotencyKey { get; init; }
}
