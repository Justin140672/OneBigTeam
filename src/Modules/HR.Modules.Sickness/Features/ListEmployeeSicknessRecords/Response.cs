using HR.Modules.Sickness.Domain;

namespace HR.Modules.Sickness.Features.ListEmployeeSicknessRecords;

internal sealed record ListEmployeeSicknessRecordsResponse(
    IReadOnlyList<SicknessRecordSummary> Records);

internal sealed record SicknessRecordSummary(
    Guid Id,
    Guid CompanyId,
    Guid EmployeeId,
    Guid CategoryId,
    SicknessStatus Status,
    DateOnly StartDate,
    SicknessDayPart StartDayPart,
    DateOnly? EndDate,
    decimal? TotalDays,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
