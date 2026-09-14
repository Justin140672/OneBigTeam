namespace HR.Modules.Documents.Features.DeleteEmployeeDocument;

internal sealed record DeleteEmployeeDocumentRequest
{
    public Guid CompanyId { get; init; }
    public Guid EmployeeId { get; init; }
    public Guid EmployeeDocumentId { get; init; }

    // DOC-04: optional — "normal deletion" (this endpoint) archives rather than hard-deletes, and
    // the acceptance criteria only requires a reason to be captured "where applicable"; callers
    // that don't supply one still get a valid archive.
    public string? Reason { get; init; }

    // Populated by the endpoint from the optional "Idempotency-Key" request header (ticket 3, P1
    // follow-up). Null when the caller didn't supply one, in which case no dedup is attempted.
    internal string? IdempotencyKey { get; init; }
}
