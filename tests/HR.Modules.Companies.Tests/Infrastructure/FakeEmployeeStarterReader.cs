using HR.Infrastructure.Abstractions;
using HR.SharedKernel;

namespace HR.Modules.Companies.Tests.Infrastructure;

/// <summary>
/// Minimal test double for <see cref="IEmployeeStarterReader"/> — returns a pre-configured total
/// count so <c>GetCustomerBillingBreakdownHandler</c> tests can assert
/// <c>FutureStarters</c> without a real reporting query. Mirrors
/// <see cref="FakeEmployeeDirectoryReader"/> in this same folder.
/// </summary>
internal sealed class FakeEmployeeStarterReader : IEmployeeStarterReader
{
    public int TotalCountToReturn { get; set; }

    public Guid? LastCompanyId { get; private set; }

    public ReportFilterCriteria? LastFilter { get; private set; }

    public Pagination? LastPagination { get; private set; }

    public Task<PagedResult<EmployeeStarterReportItem>> GetEmployeeStartersAsync(
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

        return Task.FromResult(new PagedResult<EmployeeStarterReportItem>(
            [], TotalCountToReturn, pagination.PageNumber, pagination.PageSize));
    }
}
