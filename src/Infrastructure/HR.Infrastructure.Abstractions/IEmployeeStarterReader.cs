using HR.SharedKernel;

namespace HR.Infrastructure.Abstractions;

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
