using HR.Modules.Companies.Domain;

namespace HR.Modules.Companies.Features.UpdateCompanyProfile;

internal sealed record UpdateCompanyProfileResponse(
    Guid Id,
    string Name,
    string Slug,
    bool IsActive,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    CompanyBrandingMetadataResponse Branding,
    IReadOnlyCollection<UpdateCompanyAddressResponse> Addresses);

internal sealed record CompanyBrandingMetadataResponse(
    string? PrimaryLogoUrl,
    string? SmallLogoUrl,
    string? EmailLogoUrl,
    string PrimaryColor,
    string SecondaryColor,
    string AccentColor,
    DateTimeOffset UpdatedAt);

internal sealed record UpdateCompanyAddressResponse(
    Guid Id,
    CompanyAddressType Type,
    string Line1,
    string? Line2,
    string City,
    string? Region,
    string? PostalCode,
    string CountryCode);
