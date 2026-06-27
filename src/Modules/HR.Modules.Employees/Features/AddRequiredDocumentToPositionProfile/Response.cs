namespace HR.Modules.Employees.Features.AddRequiredDocumentToPositionProfile;

internal sealed record AddRequiredDocumentResponse(
    Guid Id,
    Guid PositionProfileId,
    Guid DocumentTypeId,
    bool IsMandatory,
    int? DueDaysAfterStart,
    bool RequiresExpiryDate);
