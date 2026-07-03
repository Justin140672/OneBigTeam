namespace HR.Modules.Sickness.Features.GetTeamSicknessToday;

internal sealed record GetTeamSicknessTodayResponse(IReadOnlyList<TeamSicknessTodayItem> Items);

internal sealed record TeamSicknessTodayItem(
    Guid RecordId,
    Guid EmployeeId,
    Guid CategoryId,
    DateOnly StartDate,
    string EvidenceStatus);
