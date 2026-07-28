namespace HR.Modules.Identity.Features.ResendInvite;

internal sealed record ResendInviteResponse(Guid InviteId, DateTimeOffset ExpiresAt);
