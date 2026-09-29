using HR.SharedKernel;

namespace HR.Infrastructure.Abstractions;

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
