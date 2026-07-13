namespace HR.Modules.Documents.Features.UpdateSharedCompanyDocumentMetadata;

// Deliberately only the fields the spec allows editing: Title, Description, Category,
// EffectiveDate, ReviewDate. CompanyId/DocumentId are routing identifiers, not editable data.
// There is no way to submit CreatedBy, CreatedAt, or VersionNumber here — the request shape
// itself is the enforcement, not just a runtime check.
internal sealed record UpdateSharedCompanyDocumentMetadataRequest
{
    public Guid CompanyId { get; init; }
    public Guid DocumentId { get; init; }
    public string Title { get; init; } = string.Empty;
    public string? Description { get; init; }
    public Guid CategoryId { get; init; }
    public DateOnly? EffectiveDate { get; init; }
    public DateOnly? ReviewDate { get; init; }
}
