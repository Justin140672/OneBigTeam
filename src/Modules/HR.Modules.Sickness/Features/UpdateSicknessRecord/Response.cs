using HR.Modules.Sickness.Domain;

namespace HR.Modules.Sickness.Features.UpdateSicknessRecord;

internal sealed record UpdateSicknessRecordResponse(
    Guid Id,
    Guid CompanyId,
    Guid EmployeeId,
    Guid CategoryId,
    SicknessStatus Status,
    DateOnly StartDate,
    SicknessDayPart StartDayPart,
    DateOnly? EndDate,
    SicknessDayPart? EndDayPart,
    DateOnly? ReturnToWorkDate,
    SicknessEvidenceStatus EvidenceStatus,
    string? EvidenceNotes,
    string? Notes,
    decimal? TotalDays,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
