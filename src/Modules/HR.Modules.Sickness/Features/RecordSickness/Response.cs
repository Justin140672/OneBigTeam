using HR.Modules.Sickness.Domain;

namespace HR.Modules.Sickness.Features.RecordSickness;

internal sealed record RecordSicknessResponse(
    Guid Id,
    Guid CompanyId,
    Guid EmployeeId,
    Guid CategoryId,
    SicknessStatus Status,
    DateOnly StartDate,
    SicknessDayPart StartDayPart,
    SicknessEvidenceStatus EvidenceStatus,
    string? Notes,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
