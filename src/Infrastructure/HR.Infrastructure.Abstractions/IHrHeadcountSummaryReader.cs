namespace HR.Infrastructure.Abstractions;

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
    decimal TotalFte,
    int OtherEmployees = 0);

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
