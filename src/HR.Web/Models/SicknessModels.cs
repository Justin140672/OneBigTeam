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

public sealed record CurrentSicknessAbsenceItem(
    Guid RecordId,
    Guid EmployeeId,
    Guid CategoryId,
    DateOnly StartDate,
    string EvidenceStatus);

public sealed record GetCurrentSicknessAbsencesResponseModel(List<CurrentSicknessAbsenceItem> Items);

public sealed record TeamSicknessTodayItem(
    Guid RecordId,
    Guid EmployeeId,
    Guid CategoryId,
    DateOnly StartDate,
    string EvidenceStatus);

public sealed record GetTeamSicknessTodayResponseModel(List<TeamSicknessTodayItem> Items);

public sealed record OverdueReturnToWorkReviewItem(
    Guid ReviewId,
    Guid EmployeeId,
    Guid SicknessRecordId,
    DateOnly DueDate);

public sealed record GetOverdueReturnToWorkReviewsResponseModel(List<OverdueReturnToWorkReviewItem> Items);

public sealed record MissingFitNoteItem(
    Guid RequestId,
    Guid EmployeeId,
    Guid SicknessRecordId,
    DateOnly DueDate,
    string Status);

public sealed record GetMissingFitNotesResponseModel(List<MissingFitNoteItem> Items);
