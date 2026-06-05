namespace HR.Modules.Companies.Features.UpdateCompanyProfile;

internal sealed record UpdateCompanyProfileResponse(
    Guid Id,
    string Name,
    string Slug,
    bool IsActive,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);