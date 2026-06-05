using HR.Modules.Companies.Domain;

namespace HR.Modules.Companies.Features.CreateCompany;

internal sealed record CreateCompanyResponse(
    Guid Id,
    string Name,
    string Slug,
    bool IsActive,
    DateTimeOffset CreatedAt,
    CompanyBrandingMetadataResponse Branding,
    IReadOnlyCollection<CreateCompanyAddressResponse> Addresses);

internal sealed record CompanyBrandingMetadataResponse(
    string? PrimaryLogoUrl,
    string? SmallLogoUrl,
    string? EmailLogoUrl,
    string PrimaryColor,
    string SecondaryColor,
    string AccentColor,
    DateTimeOffset UpdatedAt);

internal sealed record CreateCompanyAddressResponse(
    Guid Id,
    CompanyAddressType Type,
    string Line1,
    string? Line2,
    string City,
    string? Region,
    string? PostalCode,
    string CountryCode);
