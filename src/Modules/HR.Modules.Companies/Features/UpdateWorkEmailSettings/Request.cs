using HR.Modules.Companies.Contracts;

namespace HR.Modules.Companies.Features.UpdateWorkEmailSettings;

internal sealed record UpdateWorkEmailSettingsRequest
{
    public Guid CompanyId { get; init; }
    public bool SuggestionsEnabled { get; init; }
    public string? PrimaryDomain { get; init; }
    public WorkEmailNamingConvention NamingConvention { get; init; } = WorkEmailNamingConvention.FirstNameDotLastName;
    public int Version { get; init; }
}
