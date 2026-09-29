using HR.Infrastructure.Abstractions;
using HR.SharedKernel;

namespace HR.Modules.Reporting.Tests.Infrastructure;

internal sealed class FakeEmployeeStarterReader : IEmployeeStarterReader
{
    private readonly IReadOnlyList<EmployeeStarterReportItem> _items;
    private readonly int _totalCount;

    public FakeEmployeeStarterReader(IReadOnlyList<EmployeeStarterReportItem> items, int? totalCount = null)
    {
        _items = items;
        _totalCount = totalCount ?? items.Count;
    }

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
            _items, _totalCount, pagination.PageNumber, pagination.PageSize));
    }
}
