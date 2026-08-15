using HR.Modules.Identity.Domain;

namespace HR.Modules.Identity.Features.CreatePlatformAdministrator;

internal sealed record CreatePlatformAdministratorRequest(string Email, PlatformAdministratorRole Role);
