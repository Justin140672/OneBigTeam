namespace HR.Modules.Recruitment.Features.RespondToInternalOffer;

internal sealed record RespondToInternalOfferRequest
{
    public Guid CompanyId { get; init; }
    public Guid ApplicationId { get; init; }
    public string Decision { get; init; } = string.Empty;
    public int OfferVersion { get; init; }
    public string? Reason { get; init; }
}
