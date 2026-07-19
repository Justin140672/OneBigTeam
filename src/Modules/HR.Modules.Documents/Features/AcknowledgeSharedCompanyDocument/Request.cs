namespace HR.Modules.Documents.Features.AcknowledgeSharedCompanyDocument;

internal sealed record AcknowledgeSharedCompanyDocumentRequest
{
    public Guid CompanyId { get; init; }
    public Guid DocumentId { get; init; }

    // Set when the employee reached this document via a task (e.g. the "Acknowledge: ..." task
    // created at publish time) — recorded on the acknowledgement row as provenance, not verified
    // against the Tasks module (see SharedCompanyDocumentAcknowledgement.TaskId's doc comment).
    public Guid? TaskId { get; init; }

    // Must be true — enforced by AcknowledgeSharedCompanyDocumentValidator. Server-side mirror of
    // the UI's confirmation checkbox so the API can't be called directly to acknowledge without it.
    public bool Confirmed { get; init; }
}
