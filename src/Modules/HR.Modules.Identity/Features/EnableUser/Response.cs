namespace HR.Modules.Identity.Features.EnableUser;

internal sealed record EnableUserResponse(Guid UserId, bool IsActive);
