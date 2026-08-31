namespace HR.Modules.Identity.Features.ResetPlatformAdministratorMfa;

/// <summary>
/// Returned only after the identity provider (Supabase) has accepted removal of the administrator's
/// MFA factors. <paramref name="AdministratorEmail"/> identifies the affected account.
/// <paramref name="FactorsRemoved"/> is how many enrolled factors were cleared (0 means the account
/// had none). <paramref name="NotificationDelivered"/> indicates whether the affected administrator
/// was successfully emailed about the reset.
/// </summary>
internal sealed record ResetPlatformAdministratorMfaResponse(
    Guid AdministratorId,
    string AdministratorEmail,
    int FactorsRemoved,
    bool NotificationDelivered);
