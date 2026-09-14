using HR.Modules.Identity.Domain;

namespace HR.Modules.Identity.Features.AssignPlatformAdministratorRole;

internal sealed record AssignPlatformAdministratorRoleRequest(Guid Id, PlatformAdministratorRole Role)
{
    // Populated by the endpoint from the optional "Idempotency-Key" request header (ticket 3, P1
    // follow-up). Null when the caller didn't supply one, in which case no dedup is attempted.
    internal string? IdempotencyKey { get; init; }
}
