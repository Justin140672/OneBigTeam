using HR.SharedKernel;

namespace HR.Infrastructure.Abstractions;

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
    string? Reason,
    string? OffboardingStatus,
    string AccountStatus);
