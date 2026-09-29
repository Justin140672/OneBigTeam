namespace HR.Modules.Offboarding.Features.StartOffboarding;

internal sealed record StartOffboardingRequest(
    Guid CompanyId,
    Guid EmployeeId,
    DateOnly LastWorkingDay,
    string? Notes,
    Guid? ReplacementManagerEmployeeId = null,
    Guid? ActorEmployeeId = null)
{
    // Populated by the endpoint from the optional "Idempotency-Key" request header (ticket 3, P1
    // follow-up). Null when the caller didn't supply one, in which case no dedup is attempted.
    internal string? IdempotencyKey { get; init; }
}
