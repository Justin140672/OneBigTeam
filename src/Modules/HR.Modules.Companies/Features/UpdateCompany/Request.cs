using HR.Modules.Companies.Domain;

namespace HR.Modules.Companies.Features.UpdateCompany;

internal sealed record UpdateCompanyRequest
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public List<UpdateCompanyAddressRequest> Addresses { get; init; } = [];
    public UpdateCompanyBrandingRequest? Branding { get; init; }
}

internal sealed record UpdateCompanyAddressRequest
{
    public CompanyAddressType Type { get; init; }
    public string Line1 { get; init; } = string.Empty;
    public string? Line2 { get; init; }
    public string City { get; init; } = string.Empty;
    public string? Region { get; init; }
    public string? PostalCode { get; init; }
    public string CountryCode { get; init; } = string.Empty;
}

internal sealed record UpdateCompanyBrandingRequest
{
    public string PrimaryColor { get; init; } = string.Empty;
    public string SecondaryColor { get; init; } = string.Empty;
    public string AccentColor { get; init; } = string.Empty;
}
