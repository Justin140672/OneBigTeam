using HR.Modules.Companies.Domain;

namespace HR.Modules.Companies.Features.GetCompany;

internal sealed record GetCompanyResponse(
    Guid Id,
    string Name,
    string Slug,
    bool IsActive,
    DateTimeOffset CreatedAt,
    IReadOnlyCollection<GetCompanyAddressResponse> Addresses,
    GetCompanyBrandingResponse Branding);

internal sealed record GetCompanyBrandingResponse(
    string PrimaryColor,
    string SecondaryColor,
    string AccentColor,
    string? PrimaryLogoUrl,
    string? SmallLogoUrl,
    string? EmailLogoUrl);

internal sealed record GetCompanyAddressResponse(
    Guid Id,
    CompanyAddressType Type,
    string Line1,
    string? Line2,
    string City,
    string? Region,
    string? PostalCode,
    string CountryCode);