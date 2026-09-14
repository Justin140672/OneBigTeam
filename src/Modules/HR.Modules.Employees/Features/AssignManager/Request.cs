namespace HR.Modules.Employees.Features.AssignManager;

internal sealed record AssignManagerRequest
{
    public Guid CompanyId { get; init; }
    public Guid Id { get; init; }

    /// <summary>Null to remove the manager assignment.</summary>
    public Guid? ManagerId { get; init; }

    // Populated by the endpoint from the optional "Idempotency-Key" request header (ticket 3, P1
    // follow-up). Null when the caller didn't supply one, in which case no dedup is attempted.
    internal string? IdempotencyKey { get; init; }
}
