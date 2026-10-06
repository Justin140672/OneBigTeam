namespace HR.Modules.Recruitment.Features.GetInternalOffer;

internal sealed record GetInternalOfferRequest
{
    public Guid CompanyId { get; init; }
    public Guid ApplicationId { get; init; }
}
