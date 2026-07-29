using HR.Infrastructure.Abstractions;
using HR.SharedKernel;

namespace HR.Modules.Reporting.Tests.Infrastructure;

/// <summary>
/// Hand-rolled fake for <see cref="IEmployeeDirectoryReader"/> — records the arguments it was
/// called with so handler tests can assert the filter/pagination/sort mapping, and returns a
/// pre-configured page of items.
/// </summary>
internal sealed class FakeEmployeeDirectoryReader : IEmployeeDirectoryReader
{
    private readonly IReadOnlyList<EmployeeDirectoryReportItem> _items;
    private readonly int _totalCount;

    public FakeEmployeeDirectoryReader(IReadOnlyList<EmployeeDirectoryReportItem> items, int? totalCount = null)
    {
        _items = items;
        _totalCount = totalCount ?? items.Count;
    }

    public Guid? LastCompanyId { get; private set; }
    public ReportFilterCriteria? LastFilter { get; private set; }
    public Pagination? LastPagination { get; private set; }
    public string? LastSortBy { get; private set; }
    public bool LastSortDescending { get; private set; }

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
        LastSortBy = sortBy;
        LastSortDescending = sortDescending;

        return Task.FromResult(new PagedResult<EmployeeDirectoryReportItem>(
            _items, _totalCount, pagination.PageNumber, pagination.PageSize));
    }
}
