namespace HR.Web.Models;

public sealed record ReturnToWorkReviewDetailModel(
    Guid Id,
    Guid CompanyId,
    Guid SicknessRecordId,
    Guid EmployeeId,
    DateOnly DueDate,
    string Status,
    DateTimeOffset? CompletedAt,
    string? Notes);
