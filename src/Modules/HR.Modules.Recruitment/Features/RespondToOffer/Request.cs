namespace HR.Modules.Recruitment.Features.RespondToOffer;

internal sealed record RespondToOfferRequest
{
    public Guid CompanyId { get; init; }
    public Guid VacancyId { get; init; }
    public Guid ApplicationId { get; init; }

    // "Accepted" | "Declined" | "Withdrawn". "AwaitingResponse" is not a valid target here — it is
    // the state an offer starts in when made. Case-insensitive.
    public string Status { get; init; } = string.Empty;
}
