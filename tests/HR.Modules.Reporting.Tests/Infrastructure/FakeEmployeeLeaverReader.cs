using HR.Infrastructure.Abstractions;
using HR.SharedKernel;

namespace HR.Modules.Reporting.Tests.Infrastructure;

internal sealed class FakeEmployeeLeaverReader : IEmployeeLeaverReader
{
    private readonly IReadOnlyList<EmployeeLeaverReportItem> _items;
    private readonly int _totalCount;

    public FakeEmployeeLeaverReader(IReadOnlyList<EmployeeLeaverReportItem> items, int? totalCount = null)
    {
        _items = items;
        _totalCount = totalCount ?? items.Count;
    }

    public Guid? LastCompanyId { get; private set; }
    public ReportFilterCriteria? LastFilter { get; private set; }
    public Pagination? LastPagination { get; private set; }

    public Task<PagedResult<EmployeeLeaverReportItem>> GetEmployeeLeaversAsync(
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

        return Task.FromResult(new PagedResult<EmployeeLeaverReportItem>(
            _items, _totalCount, pagination.PageNumber, pagination.PageSize));
    }
}
