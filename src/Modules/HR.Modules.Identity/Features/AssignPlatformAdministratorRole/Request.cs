using HR.Modules.Identity.Domain;

namespace HR.Modules.Identity.Features.AssignPlatformAdministratorRole;

internal sealed record AssignPlatformAdministratorRoleRequest(Guid Id, PlatformAdministratorRole Role);
