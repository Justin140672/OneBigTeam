using HR.Infrastructure.Abstractions;

namespace HR.Modules.Reporting.Tests.Infrastructure;

internal sealed class FakeLeaveCalendarReader : ILeaveCalendarReader
{
    private readonly IReadOnlyList<LeaveCalendarReportItem> _items;

    public FakeLeaveCalendarReader(IReadOnlyList<LeaveCalendarReportItem> items)
    {
        _items = items;
    }

    public Guid? LastCompanyId { get; private set; }
    public IReadOnlyCollection<Guid>? LastEmployeeIds { get; private set; }
    public int? LastYear { get; private set; }
    public int? LastMonth { get; private set; }

    public Task<IReadOnlyList<LeaveCalendarReportItem>> GetLeaveCalendarAsync(
        Guid companyId,
        IReadOnlyCollection<Guid>? employeeIds,
        int year,
        int month,
        CancellationToken cancellationToken)
    {
        LastCompanyId = companyId;
        LastEmployeeIds = employeeIds;
        LastYear = year;
        LastMonth = month;

        return Task.FromResult(_items);
    }
}
