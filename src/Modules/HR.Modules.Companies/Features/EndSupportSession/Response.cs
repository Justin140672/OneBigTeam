namespace HR.Modules.Companies.Features.EndSupportSession;

internal sealed record EndSupportSessionResponse(Guid SupportSessionId, DateTimeOffset RevokedAt);
