namespace HR.Modules.Companies.Features.GetCompany;

internal sealed record GetCompanyResponse(
    Guid Id,
    string Name,
    string Slug,
    bool IsActive,
    DateTimeOffset CreatedAt);