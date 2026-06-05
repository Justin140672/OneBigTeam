using HR.Modules.Companies.Domain;

namespace HR.Modules.Companies.Features.UpdateCompany;

internal sealed record UpdateCompanyResponse(
    Guid Id,
    string Name,
    string Slug,
    bool IsActive,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    IReadOnlyCollection<UpdateCompanyAddressResponse> Addresses,
    UpdateCompanyBrandingResponse? Branding);

internal sealed record UpdateCompanyAddressResponse(
    Guid Id,
    CompanyAddressType Type,
    string Line1,
    string? Line2,
    string City,
    string? Region,
    string? PostalCode,
    string CountryCode);

internal sealed record UpdateCompanyBrandingResponse(
    string PrimaryColor,
    string SecondaryColor,
    string AccentColor);
