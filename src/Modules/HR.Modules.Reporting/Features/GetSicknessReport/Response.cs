namespace HR.Modules.Reporting.Features.GetSicknessReport;

internal sealed record GetSicknessReportResponse(IReadOnlyList<SicknessReportGroupRow> Items);

// BradfordScore is always 0 for now — there is no Bradford-factor configuration in the domain yet
// (would require a per-company weighting/scoring engine). The column is included so the UI/export
// shape is stable once that scoring engine is built; do not treat 0 as a real computed score.
internal sealed record SicknessReportGroupRow(
    string GroupKey,
    string GroupLabel,
    int AbsenceCount,
    decimal DaysAbsent,
    int BradfordScore);
