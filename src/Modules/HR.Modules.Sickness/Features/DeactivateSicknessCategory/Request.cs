namespace HR.Modules.Sickness.Features.DeactivateSicknessCategory;

internal sealed record DeactivateSicknessCategoryRequest
{
    public Guid CompanyId { get; init; }
    public Guid Id { get; init; }

    // SICK-06: populated by the endpoint from the authenticated user's resolved identity — never
    // bound from the client body.
    internal Guid? ActorEmployeeId { get; init; }

    // Populated by the endpoint from the optional "Idempotency-Key" request header (ticket 3, P1
    // follow-up). Null when the caller didn't supply one, in which case no dedup is attempted.
    internal string? IdempotencyKey { get; init; }
}
