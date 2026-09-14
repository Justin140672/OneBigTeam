namespace HR.Modules.Employees.Features.AddRequiredDocumentToPositionProfile;

internal sealed record AddRequiredDocumentRequest
{
    public Guid CompanyId { get; init; }
    public Guid PositionProfileId { get; init; }
    public Guid DocumentTypeId { get; init; }
    public bool IsMandatory { get; init; }
    public int? DueDaysAfterStart { get; init; }
    public bool RequiresExpiryDate { get; init; }

    // Populated by the endpoint from the optional "Idempotency-Key" request header (ticket 3, P1
    // follow-up). Null when the caller didn't supply one, in which case no dedup is attempted.
    internal string? IdempotencyKey { get; init; }
}
