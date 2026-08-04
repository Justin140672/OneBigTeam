using HR.Infrastructure.Abstractions;
using HR.SharedKernel;

namespace HR.Modules.Companies.Tests.Infrastructure;

/// <summary>
/// Minimal test double for <see cref="IEmployeeDirectoryReader"/> — returns a pre-configured
/// total count so <c>GetSubscriptionDetailsHandler</c> tests can assert
/// <c>ActiveEmployeeCount</c> without a real reporting query. Mirrors
/// HR.Modules.Reporting.Tests.Infrastructure.FakeEmployeeDirectoryReader but lives locally since
/// module tests don't reference each other's test projects.
/// </summary>
internal sealed class FakeEmployeeDirectoryReader : IEmployeeDirectoryReader
{
    public int TotalCountToReturn { get; set; }

    public Guid? LastCompanyId { get; private set; }

    public ReportFilterCriteria? LastFilter { get; private set; }

    public Pagination? LastPagination { get; private set; }

    public Task<PagedResult<EmployeeDirectoryReportItem>> GetEmployeeDirectoryAsync(
        Guid companyId,
        ReportFilterCriteria filter,
        Pagination pagination,
        string? sortBy,
        bool sortDescending,
        CancellationToken cancellationToken)
    {
        LastCompanyId = companyId;
        LastFilter = filter;
        LastPagination = pagination;

        return Task.FromResult(new PagedResult<EmployeeDirectoryReportItem>(
            [], TotalCountToReturn, pagination.PageNumber, pagination.PageSize));
    }
}
