namespace HR.Modules.Companies.Features.GetCompany;

internal sealed record GetCompanyRequest
{
    public Guid CompanyId { get; init; }
}