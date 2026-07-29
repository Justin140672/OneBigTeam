namespace HR.Infrastructure.Abstractions;

/// <summary>
/// Company-wide (optionally employee-id-filtered) probation data for the Probation Report
/// (OBT-711), as owned by HR.Modules.Probation. Distinct from IProbationSummaryReader, which only
/// reads a single employee's latest probation record and is not sufficient for a company-wide
/// report. When employeeIds is supplied, results are restricted to those employees only — used by
/// the Reporting module to apply row-level manager scoping without this reader knowing anything
/// about callers/roles.
/// </summary>
public interface IProbationReportReader
{
    Task<IReadOnlyList<ProbationReportItem>> GetProbationReportAsync(
        Guid companyId,
        IReadOnlyCollection<Guid>? employeeIds,
        CancellationToken cancellationToken);
}

public sealed record ProbationReportItem(
    Guid EmployeeId,
    Guid RecordId,
    string Status,
    DateOnly StartDate,
    DateOnly ExpectedEndDate,
    int DueReviewCount,
    int OverdueReviewCount);
