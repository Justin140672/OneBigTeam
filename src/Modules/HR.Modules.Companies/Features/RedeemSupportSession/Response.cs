namespace HR.Modules.Companies.Features.RedeemSupportSession;

internal sealed record RedeemSupportSessionResponse(
    Guid CompanyId,
    Guid IssuedByAdminUserId,
    string IssuedByAdminEmail,
    DateTimeOffset RedeemedAt);
