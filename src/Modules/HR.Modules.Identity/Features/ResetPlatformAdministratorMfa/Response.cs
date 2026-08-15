namespace HR.Modules.Identity.Features.ResetPlatformAdministratorMfa;

/// <summary>
/// Implemented always false — this action is intentionally stubbed. See the handler's remarks for
/// what a real implementation requires.
/// </summary>
internal sealed record ResetPlatformAdministratorMfaResponse(Guid AdministratorId, bool Implemented = false);
