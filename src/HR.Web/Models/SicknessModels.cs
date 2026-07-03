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

public sealed record SicknessRecordListItemModel(
    Guid Id,
    Guid CompanyId,
    Guid EmployeeId,
    Guid CategoryId,
    string Status,
    DateOnly StartDate,
    string StartDayPart,
    DateOnly? EndDate,
    decimal? TotalDays,
    string EvidenceStatus,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record ListEmployeeSicknessRecordsResponseModel(List<SicknessRecordListItemModel> Records);

public sealed record RecordSicknessRequest(
    Guid CategoryId,
    DateOnly StartDate,
    string StartDayPart,
    DateOnly? EndDate,
    string? EndDayPart,
    string? Notes);

public sealed record CloseSicknessRecordRequest(
    DateOnly EndDate,
    string EndDayPart,
    DateOnly? ReturnToWorkDate,
    string? Notes);
