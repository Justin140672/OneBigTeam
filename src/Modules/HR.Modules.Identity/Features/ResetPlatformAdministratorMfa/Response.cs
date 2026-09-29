namespace HR.Modules.Identity.Features.ResetPlatformAdministratorMfa;

internal sealed record ResetPlatformAdministratorMfaResponse(
    Guid AdministratorId,
    string AdministratorEmail,
    int FactorsRemoved,
    bool NotificationDelivered);
