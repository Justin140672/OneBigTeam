using HR.SharedKernel;

namespace HR.Infrastructure.Abstractions;

/// <summary>
/// Provides a paged, filterable listing of newly-started employees for the Employee Starter
/// Report (OBT-704), as owned by HR.Modules.Employees. Composes Employees' own start
/// date/department/position data with cross-module recruiter/onboarding/probation data via their
/// own narrow reader contracts, so HR.Modules.Reporting never has to compose multiple readers
/// itself for this report.
/// </summary>
public interface IEmployeeStarterReader
{
    Task<PagedResult<EmployeeStarterReportItem>> GetEmployeeStartersAsync(
        Guid companyId,
        ReportFilterCriteria filter,
        Pagination pagination,
        string? sortBy,
        bool sortDescending,
        CancellationToken cancellationToken);
}

public sealed record EmployeeStarterReportItem(
    Guid EmployeeId,
    string Name,
    DateOnly StartDate,
    string? Recruiter,
    string? Department,
    string? Position,
    string? OnboardingStatus,
    string? ProbationStatus);
