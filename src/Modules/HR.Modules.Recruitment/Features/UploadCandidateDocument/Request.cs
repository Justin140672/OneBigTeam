using Microsoft.AspNetCore.Http;

namespace HR.Modules.Recruitment.Features.UploadCandidateDocument;

internal sealed class UploadCandidateDocumentRequest
{
    public Guid CompanyId { get; init; }
    public Guid CandidateId { get; init; }
    public string Title { get; init; } = string.Empty;
    public IFormFile File { get; init; } = null!;
}
