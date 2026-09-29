namespace HR.Modules.Identity.Features.ResetPlatformAdministratorMfa;

internal sealed record ResetPlatformAdministratorMfaRequest(Guid Id, bool Confirmed, string Reason);
