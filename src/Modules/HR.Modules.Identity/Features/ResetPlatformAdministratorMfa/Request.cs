namespace HR.Modules.Identity.Features.ResetPlatformAdministratorMfa;

/// <summary>
/// <paramref name="Id"/> is bound from the route. <paramref name="Confirmed"/> and
/// <paramref name="Reason"/> come from the request body — the caller must explicitly confirm the
/// action and supply an administrative reason (recorded in the audit trail).
/// </summary>
internal sealed record ResetPlatformAdministratorMfaRequest(Guid Id, bool Confirmed, string Reason);
