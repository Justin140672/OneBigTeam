namespace HR.Modules.Companies.Features.CreateCompany;

internal sealed record CreateCompanyRequest
{
    public string Name { get; init; } = string.Empty;
    public string? Slug { get; init; }
}
