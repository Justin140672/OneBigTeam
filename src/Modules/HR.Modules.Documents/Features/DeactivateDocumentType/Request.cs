namespace HR.Modules.Documents.Features.DeactivateDocumentType;

internal sealed record DeactivateDocumentTypeRequest
{
    public Guid CompanyId { get; init; }
    public Guid DocumentTypeId { get; init; }
}
