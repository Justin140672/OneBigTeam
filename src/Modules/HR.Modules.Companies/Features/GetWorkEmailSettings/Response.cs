using HR.Modules.Companies.Contracts;

namespace HR.Modules.Companies.Features.GetWorkEmailSettings;

internal sealed record WorkEmailConventionExample(WorkEmailNamingConvention Convention, string LocalPart);

internal sealed record GetWorkEmailSettingsResponse(
    Guid CompanyId,
    bool SuggestionsEnabled,
    string? PrimaryDomain,
    WorkEmailNamingConvention NamingConvention,
    string ExampleFirstName,
    string ExampleLastName,
    IReadOnlyList<WorkEmailConventionExample> Examples,
    DateTimeOffset UpdatedAt,
    int Version);
