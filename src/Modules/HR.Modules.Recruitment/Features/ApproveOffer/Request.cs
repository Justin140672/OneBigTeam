namespace HR.Modules.Recruitment.Features.ApproveOffer;

internal sealed record ApproveOfferRequest
{
    public Guid CompanyId { get; init; }
    public Guid VacancyId { get; init; }
    public Guid ApplicationId { get; init; }
}
