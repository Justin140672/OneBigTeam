namespace HR.Modules.Companies.Features.RevokeSupportSession;

internal sealed record RevokeSupportSessionResponse(Guid SupportSessionId, DateTimeOffset RevokedAt);
