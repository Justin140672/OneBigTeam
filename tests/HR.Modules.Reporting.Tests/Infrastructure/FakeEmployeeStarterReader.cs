using HR.Infrastructure.Abstractions;
using HR.SharedKernel;

namespace HR.Modules.Reporting.Tests.Infrastructure;

/// <summary>
/// Hand-rolled fake for <see cref="IEmployeeStarterReader"/> — records the arguments it was called
/// with and returns a pre-configured page of items, with an independently settable TotalCount so
/// truncation behaviour can be exercised without materialising tens of thousands of fake rows.
/// </summary>
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
