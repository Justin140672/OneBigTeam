namespace HR.Modules.Documents.Features.GetDocumentRequest;

internal sealed record GetDocumentRequestResponse(
    Guid Id,
    string DocumentTypeName,
    DateOnly? DueDate,
    string Status,
    Guid? RequestedByEmployeeId,
    string? RequestedByName,
    DateTimeOffset CreatedAt);
