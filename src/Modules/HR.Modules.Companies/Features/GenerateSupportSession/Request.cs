namespace HR.Modules.Companies.Features.GenerateSupportSession;

internal sealed record GenerateSupportSessionRequest(Guid CompanyId, string Reason);
