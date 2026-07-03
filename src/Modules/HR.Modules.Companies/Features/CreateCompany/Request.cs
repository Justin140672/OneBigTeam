using HR.Modules.Companies.Domain;

namespace HR.Modules.Companies.Features.CreateCompany;

internal sealed record CreateCompanyRequest
{
    public string Name { get; init; } = string.Empty;
    public List<CreateCompanyAddressRequest> Addresses { get; init; } = [];
}

internal sealed record CreateCompanyAddressRequest
{
    public CompanyAddressType Type { get; init; }
    public string Line1 { get; init; } = string.Empty;
    public string? Line2 { get; init; }
    public string City { get; init; } = string.Empty;
    public string? Region { get; init; }
    public string? PostalCode { get; init; }
    public string CountryCode { get; init; } = string.Empty;
}
