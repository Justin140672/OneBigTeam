namespace HR.SharedKernel.Contracts;

public sealed record PositionProfileRequiredDocumentItem(
    Guid Id,
    Guid DocumentTypeId,
    bool IsMandatory,
    int? DueDaysAfterStart,
    bool RequiresExpiryDate);
