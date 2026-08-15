using HR.Modules.Identity.Domain;

namespace HR.Modules.Identity.Features.AssignPlatformAdministratorRole;

internal sealed record AssignPlatformAdministratorRoleResponse(Guid Id, PlatformAdministratorRole Role);
