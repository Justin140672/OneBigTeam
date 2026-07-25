namespace HR.Modules.Documents.Features.GetSharedCompanyDocumentAcknowledgementProgress;

internal sealed record GetSharedCompanyDocumentAcknowledgementProgressRequest
{
    public Guid CompanyId { get; init; }
    public Guid DocumentId { get; init; }
}
