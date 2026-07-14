namespace HR.Modules.Documents.Features.GetSharedCompanyDocumentAcknowledgementProgress;

internal sealed record GetSharedCompanyDocumentAcknowledgementProgressRequest
{
    public Guid CompanyId { get; init; }
    public Guid DocumentId { get; init; }
    public Guid? DepartmentId { get; init; }
    public Guid? LocationId { get; init; }
    public bool? IsAcknowledged { get; init; }
    public bool? IsOverdue { get; init; }
}
