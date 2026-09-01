namespace HR.Modules.Companies.Features.LiftCompanyLegalHold;

internal sealed record LiftCompanyLegalHoldRequest
{
    public Guid CompanyId { get; init; }
    public string Reason { get; init; } = string.Empty;
}
