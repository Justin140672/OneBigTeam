using HR.Modules.Identity.Domain;

namespace HR.Modules.Identity.Features.ListPlatformAdministrators;

internal sealed record PlatformAdministratorSummary(
    Guid Id,
    string Email,
    PlatformAdministratorRole Role,
    bool IsEnabled,
    DateTimeOffset CreatedAt,
    DateTimeOffset? DisabledAt);

internal sealed record ListPlatformAdministratorsResponse(IReadOnlyList<PlatformAdministratorSummary> Administrators);
