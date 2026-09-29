namespace HR.Modules.Reporting.Features.GetSicknessReport;

internal sealed record GetSicknessReportResponse(
    IReadOnlyList<SicknessReportGroupRow> Items,
    int TotalCount,
    bool IsTruncated);

internal sealed record SicknessReportGroupRow(
    string GroupKey,
    string GroupLabel,
    int AbsenceCount,
    decimal DaysAbsent,
    int BradfordScore);
