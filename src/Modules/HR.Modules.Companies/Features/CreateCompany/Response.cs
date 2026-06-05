using HR.Modules.Companies.Domain;

namespace HR.Modules.Companies.Features.CreateCompany;

internal sealed record CreateCompanyResponse(
    Guid Id,
    string Name,
    string Slug,
    bool IsActive,
    DateTimeOffset CreatedAt,
    IReadOnlyCollection<CreateCompanyAddressResponse> Addresses);

internal sealed record CreateCompanyAddressResponse(
    Guid Id,
    CompanyAddressType Type,
    string Line1,
    string? Line2,
    string City,
    string? Region,
    string? PostalCode,
    string CountryCode);
