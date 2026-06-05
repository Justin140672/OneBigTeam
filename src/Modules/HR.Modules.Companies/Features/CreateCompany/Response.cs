namespace HR.Modules.Companies.Features.CreateCompany;

internal sealed record CreateCompanyResponse(
    Guid Id,
    string Name,
    string Slug,
    bool IsActive,
    DateTimeOffset CreatedAt);
