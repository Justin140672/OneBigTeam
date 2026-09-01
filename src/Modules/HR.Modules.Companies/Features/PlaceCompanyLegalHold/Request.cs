namespace HR.Modules.Companies.Features.PlaceCompanyLegalHold;

internal sealed record PlaceCompanyLegalHoldRequest
{
    public Guid CompanyId { get; init; }
    public string Reason { get; init; } = string.Empty;
}
