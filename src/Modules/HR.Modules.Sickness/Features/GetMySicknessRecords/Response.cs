using HR.Modules.Sickness.Domain;

namespace HR.Modules.Sickness.Features.GetMySicknessRecords;

internal sealed record GetMySicknessRecordsResponse(
    IReadOnlyList<MySicknessRecordSummary> Records);

internal sealed record MySicknessRecordSummary(
    Guid Id,
    Guid CompanyId,
    Guid EmployeeId,
    Guid CategoryId,
    SicknessStatus Status,
    DateOnly StartDate,
    SicknessDayPart StartDayPart,
    DateOnly? EndDate,
    decimal? TotalDays,
    SicknessEvidenceStatus EvidenceStatus,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
