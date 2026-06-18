namespace HR.Modules.Documents.Features.UpdateDocumentType;

internal sealed record UpdateDocumentTypeRequest
{
    public Guid CompanyId { get; init; }
    public Guid DocumentTypeId { get; init; }
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
}
