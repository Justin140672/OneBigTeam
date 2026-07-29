using HR.SharedKernel;

namespace HR.Infrastructure.Abstractions;

/// <summary>
/// Provides a paged, filterable listing of leaving/left employees for the Employee Leaver Report
/// (OBT-705), as owned by HR.Modules.Employees. Composes Employees' own leaving date/department/
/// position data with cross-module offboarding/account-status data via their own narrow reader
/// contracts.
/// </summary>
public interface IEmployeeLeaverReader
{
    Task<PagedResult<EmployeeLeaverReportItem>> GetEmployeeLeaversAsync(
        Guid companyId,
        ReportFilterCriteria filter,
        Pagination pagination,
        string? sortBy,
        bool sortDescending,
        CancellationToken cancellationToken);
}

public sealed record EmployeeLeaverReportItem(
    Guid EmployeeId,
    string Name,
    DateOnly? LeavingDate,
    DateOnly? LastWorkingDay,
    string? Department,
    string? Position,
    // No leaving-reason field exists anywhere in the current domain model (Employee has no
    // reason column and OffboardingPlan does not capture one either) — always null until a
    // reason capture mechanism is added. Kept in the DTO because the report explicitly asks for
    // it "when available" per OBT-705.
    string? Reason,
    string? OffboardingStatus,
    string AccountStatus);
