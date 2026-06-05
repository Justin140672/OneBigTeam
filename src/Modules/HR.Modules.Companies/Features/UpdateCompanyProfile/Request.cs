namespace HR.Modules.Companies.Features.UpdateCompanyProfile;

internal sealed record UpdateCompanyProfileRequest
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
}