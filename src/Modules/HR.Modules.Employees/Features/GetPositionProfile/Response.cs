namespace HR.Modules.Employees.Features.GetPositionProfile;

internal sealed record GetPositionProfileResponse(
    Guid Id,
    Guid CompanyId,
    Guid? DepartmentId,
    string Title,
    string? Description,
    bool IsManagerial,
    bool IsActive,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    IReadOnlyList<RequiredDocumentItem> RequiredDocuments);

internal sealed record RequiredDocumentItem(
    Guid Id,
    Guid DocumentTypeId,
    bool IsMandatory,
    int? DueDaysAfterStart,
    bool RequiresExpiryDate);
