namespace HR.Modules.Employees.Contracts;

public sealed record PositionProfileRequiredDocumentItem(
    Guid Id,
    Guid DocumentTypeId,
    bool IsMandatory,
    int? DueDaysAfterStart,
    bool RequiresExpiryDate);
