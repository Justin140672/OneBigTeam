namespace HR.Modules.Employees.Features.ListRequiredDocumentsForPositionProfile;

internal sealed record ListRequiredDocumentsResponse(IReadOnlyList<RequiredDocumentListItem> Items);

internal sealed record RequiredDocumentListItem(
    Guid Id,
    Guid DocumentTypeId,
    string DocumentTypeName,
    bool IsMandatory,
    int? DueDaysAfterStart,
    bool RequiresExpiryDate);
