namespace HR.Modules.Reporting.Features.GetSicknessReport;

internal sealed record GetSicknessReportResponse(
    IReadOnlyList<SicknessReportGroupRow> Items,
    int TotalCount,
    bool IsTruncated);

// SICK-04: BradfordScore is now a genuine Bradford Factor calculation — S^2 * D, where S is the
// number of separate absence spells (AbsenceCount) and D is total days absent (DaysAbsent), both
// already computed for this group over the report's own requested date range (Request.StartDate /
// Request.EndDate). The platform does not currently enforce a fixed rolling window (the classic
// formula commonly uses a trailing 52 weeks) — the caller's chosen report date range *is* the
// window, so selecting the last 12 months on this report reproduces the standard calculation.
// See GetSicknessReportHandler for the computation.
internal sealed record SicknessReportGroupRow(
    string GroupKey,
    string GroupLabel,
    int AbsenceCount,
    decimal DaysAbsent,
    int BradfordScore);
