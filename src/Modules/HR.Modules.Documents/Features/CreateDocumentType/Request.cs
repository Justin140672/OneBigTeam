namespace HR.Modules.Documents.Features.CreateDocumentType;

internal sealed record CreateDocumentTypeRequest
{
    public Guid CompanyId { get; init; }
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
}
