using HR.SharedKernel;

namespace HR.Infrastructure.Abstractions;

/// <summary>
/// Provides a paged, filterable, sortable company-wide employee directory listing, as owned by
/// HR.Modules.Employees. Used by HR.Modules.Reporting to build the Employee Directory Report
/// without a direct module-to-module reference or database join.
/// </summary>
public interface IEmployeeDirectoryReader
{
    Task<PagedResult<EmployeeDirectoryReportItem>> GetEmployeeDirectoryAsync(
        Guid companyId,
        ReportFilterCriteria filter,
        Pagination pagination,
        string? sortBy,
        bool sortDescending,
        CancellationToken cancellationToken);
}

public sealed record EmployeeDirectoryReportItem(
    Guid EmployeeId,
    string EmployeeNumber,
    string Name,
    string? Department,
    string? Position,
    string? Manager,
    string? EmploymentType,
    DateOnly StartDate,
    string Status,
    string? WorkLocation,
    string Email);
