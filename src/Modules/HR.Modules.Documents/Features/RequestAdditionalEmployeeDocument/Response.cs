namespace HR.Modules.Documents.Features.RequestAdditionalEmployeeDocument;

internal sealed record RequestAdditionalEmployeeDocumentResponse(
    Guid     DocumentRequestId,
    Guid     CompanyId,
    Guid     EmployeeId,
    Guid     DocumentTypeId,
    string   DocumentTypeName,
    DateOnly? DueDate,
    string   Status,
    DateTimeOffset CreatedAt);
