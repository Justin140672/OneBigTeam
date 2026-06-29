namespace HR.SharedKernel.Contracts;

public sealed record PositionProfileRequiredDocumentItem(
    Guid DocumentTypeId,
    bool IsMandatory,
    int? DueDaysAfterStart,
    bool RequiresExpiryDate);
