namespace HR.Infrastructure.Abstractions;

public sealed record OutstandingDocumentRequestItem(
    Guid Id,
    string DocumentTypeName,
    DateOnly? DueDate,
    bool IsMandatory);
