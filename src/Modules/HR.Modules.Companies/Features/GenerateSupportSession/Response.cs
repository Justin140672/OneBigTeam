namespace HR.Modules.Companies.Features.GenerateSupportSession;

internal sealed record GenerateSupportSessionResponse(
    Guid SupportSessionId,
    Guid CompanyId,
    DateTimeOffset ExpiresAt,
    string Token);
