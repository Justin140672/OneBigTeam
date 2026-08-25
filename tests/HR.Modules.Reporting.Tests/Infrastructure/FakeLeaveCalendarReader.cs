using HR.Infrastructure.Abstractions;

namespace HR.Modules.Reporting.Tests.Infrastructure;

/// <summary>
/// Hand-rolled fake for <see cref="ILeaveCalendarReader"/> — records the arguments it was called
/// with and returns a pre-configured, unpaged set of items (matching the real reader's contract of
/// returning unpaged rows for the requested month, with row-cap enforcement left to the caller).
/// </summary>
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
