using Microsoft.AspNetCore.Http;

namespace HR.Modules.Documents.Features.UploadSharedCompanyDocumentVersion;

internal sealed class UploadSharedCompanyDocumentVersionRequest
{
    public Guid CompanyId { get; init; }
    public Guid DocumentId { get; init; }
    public string VersionNote { get; init; } = string.Empty;
    public bool RequiresReacknowledgement { get; init; }

    // Optional — the acknowledgement statement is locked once a document is Published (see
    // UpdateSharedCompanyDocumentAcknowledgementSettingsHandler), so uploading a new version that
    // requires re-acknowledgement is the only route left to change the wording. Ignored unless
    // RequiresReacknowledgement is also true; when omitted, the document keeps its existing
    // statement.
    public string? AcknowledgementStatement { get; init; }

    public IFormFile File { get; init; } = null!;
}
