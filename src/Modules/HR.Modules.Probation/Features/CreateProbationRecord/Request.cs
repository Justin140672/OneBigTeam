namespace HR.Modules.Probation.Features.CreateProbationRecord;

internal sealed record CreateProbationRecordRequest
{
    public Guid CompanyId { get; init; }
    public Guid EmployeeId { get; init; }
    public Guid ManagerEmployeeId { get; init; }
    public DateOnly StartDate { get; init; }
    public DateOnly ExpectedEndDate { get; init; }
    public string? Notes { get; init; }

    internal Guid? ActorEmployeeId { get; init; }

    // Populated by the endpoint from the optional "Idempotency-Key" request header (ticket 3, P1
    // follow-up). Null when the caller didn't supply one, in which case no dedup is attempted.
    internal string? IdempotencyKey { get; init; }
}
