using HR.Modules.Companies.Contracts;

namespace HR.Modules.Companies.Features.UpdateWorkEmailSettings;

internal sealed record UpdateWorkEmailSettingsResponse(
    Guid CompanyId,
    bool SuggestionsEnabled,
    string? PrimaryDomain,
    WorkEmailNamingConvention NamingConvention,
    DateTimeOffset UpdatedAt,
    int Version);
