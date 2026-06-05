namespace HR.Modules.Companies.Features.GetCompany;

internal sealed record GetCompanyRequest
{
    public Guid Id { get; init; }
}