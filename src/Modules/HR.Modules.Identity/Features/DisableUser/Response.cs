namespace HR.Modules.Identity.Features.DisableUser;

internal sealed record DisableUserResponse(Guid UserId, bool IsActive);
