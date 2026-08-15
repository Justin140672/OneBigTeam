using HR.Modules.Identity.Domain;

namespace HR.Modules.Identity.Features.CreatePlatformAdministrator;

internal sealed record CreatePlatformAdministratorResponse(
    Guid Id,
    string Email,
    PlatformAdministratorRole Role,
    bool IsEnabled,
    DateTimeOffset CreatedAt);
