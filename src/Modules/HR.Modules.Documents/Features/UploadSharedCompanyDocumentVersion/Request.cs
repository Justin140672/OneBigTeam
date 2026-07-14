using Microsoft.AspNetCore.Http;

namespace HR.Modules.Documents.Features.UploadSharedCompanyDocumentVersion;

internal sealed class UploadSharedCompanyDocumentVersionRequest
{
    public Guid CompanyId { get; init; }
    public Guid DocumentId { get; init; }
    public string VersionNote { get; init; } = string.Empty;
    public bool RequiresReacknowledgement { get; init; }
    public IFormFile File { get; init; } = null!;
}
