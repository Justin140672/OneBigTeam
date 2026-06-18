namespace HR.Modules.Documents.Features.ListDocumentTypes;

internal sealed record ListDocumentTypesRequest
{
    public Guid CompanyId { get; init; }
    public bool IncludeInactive { get; init; } = false;
    public bool EmployeeUploadOnly { get; init; } = false;
}
