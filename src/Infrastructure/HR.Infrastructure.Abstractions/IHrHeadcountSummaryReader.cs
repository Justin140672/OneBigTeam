namespace HR.Infrastructure.Abstractions;

/// <summary>
/// Company-wide headcount data for the HR Headcount Summary Report, as owned by
/// HR.Modules.Employees. Employee status reuses the existing EmploymentStatus enum (via
/// EmployeeStatus.ToString()) rather than introducing new classification logic — see
/// HrHeadcountSummaryItem's remarks. FTE is resolved from each employee's current Compensation
/// record (EffectiveFrom &lt;= today &amp;&amp; (EffectiveTo == null || EffectiveTo &gt;= today)); it is
/// sensitive/salary-adjacent data and must never be logged.
/// </summary>
public interface IHrHeadcountSummaryReader
{
    Task<HrHeadcountSummaryResult> GetHeadcountSummaryAsync(
        Guid companyId,
        ReportFilterCriteria filter,
        CancellationToken cancellationToken);
}

public sealed record HrHeadcountSummaryResult(
    IReadOnlyList<HrHeadcountSummaryItem> Items,
    int TotalHeadcount,
    int ActiveEmployees,
    int FutureStarters,
    int Leavers,
    decimal TotalFte);

public sealed record HrHeadcountSummaryItem(
    Guid EmployeeId,
    string EmployeeName,
    string? Department,
    string? Location,
    string? Position,
    string? EmploymentType,
    string Status,
    DateOnly StartDate,
    DateOnly? LeavingDate,
    decimal? Fte);
