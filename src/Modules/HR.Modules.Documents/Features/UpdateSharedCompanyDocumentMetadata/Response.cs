namespace HR.Modules.Documents.Features.UpdateSharedCompanyDocumentMetadata;

internal sealed record UpdateSharedCompanyDocumentMetadataResponse(
    Guid Id,
    Guid CompanyId,
    string Title,
    string? Description,
    Guid CategoryId,
    int VersionNumber,
    string Status,
    DateOnly? EffectiveDate,
    DateOnly? ReviewDate,
    Guid UpdatedBy,
    DateTimeOffset UpdatedAt);
