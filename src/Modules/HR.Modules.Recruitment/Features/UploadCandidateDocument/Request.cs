using Microsoft.AspNetCore.Http;

namespace HR.Modules.Recruitment.Features.UploadCandidateDocument;

internal sealed class UploadCandidateDocumentRequest
{
    public Guid CompanyId { get; init; }
    public Guid CandidateId { get; init; }
    public string Title { get; init; } = string.Empty;

    // Ticket 1: "Cv" marks this upload as the candidate's CV (the Review CV workflow shows the most
    // recently uploaded CV). Optional; defaults to "Other" for a general supporting document.
    public string? Kind { get; init; }

    public IFormFile File { get; init; } = null!;
}
