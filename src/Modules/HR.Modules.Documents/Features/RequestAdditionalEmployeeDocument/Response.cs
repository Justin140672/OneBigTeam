namespace HR.Modules.Documents.Features.RequestAdditionalEmployeeDocument;

internal sealed record RequestAdditionalEmployeeDocumentResponse(
    Guid     DocumentRequestId,
    Guid     CompanyId,
    Guid     EmployeeId,
    Guid     DocumentTypeId,
    string   DocumentTypeName,
    DateOnly? DueDate,
    bool     IsMandatory,
    string?  Notes,
    string   Status,
    DateTimeOffset CreatedAt);
