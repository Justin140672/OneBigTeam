namespace HR.Modules.Sickness.Features.GetCurrentSicknessAbsences;

internal sealed record GetCurrentSicknessAbsencesResponse(IReadOnlyList<CurrentSicknessAbsenceItem> Items);

internal sealed record CurrentSicknessAbsenceItem(
    Guid RecordId,
    Guid EmployeeId,
    Guid CategoryId,
    DateOnly StartDate,
    string EvidenceStatus);
