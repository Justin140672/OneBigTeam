namespace HR.Infrastructure.Abstractions;

public sealed record PositionProfileRequiredDocumentItem(
    Guid Id,
    Guid DocumentTypeId,
    bool IsMandatory,
    int? DueDaysAfterStart,
    bool RequiresExpiryDate);
