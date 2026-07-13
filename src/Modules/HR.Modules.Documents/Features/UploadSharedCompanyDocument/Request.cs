using Microsoft.AspNetCore.Http;

namespace HR.Modules.Documents.Features.UploadSharedCompanyDocument;

internal sealed class UploadSharedCompanyDocumentRequest
{
    public Guid CompanyId { get; init; }
    public string Title { get; init; } = string.Empty;
    public string? Description { get; init; }
    public Guid CategoryId { get; init; }
    public DateOnly? EffectiveDate { get; init; }
    public DateOnly? ReviewDate { get; init; }
    public Guid? AudienceDepartmentId { get; init; }
    public Guid? AudienceLocationId { get; init; }
    public bool RequiresAcknowledgement { get; init; }
    public IFormFile File { get; init; } = null!;
}
