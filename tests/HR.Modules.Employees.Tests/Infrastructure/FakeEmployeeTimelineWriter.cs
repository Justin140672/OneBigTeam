using HR.Modules.Employees.Domain;
using HR.Modules.Employees.Services;

namespace HR.Modules.Employees.Tests.Infrastructure;

internal sealed class FakeEmployeeTimelineWriter : IEmployeeTimelineWriter
{
    private readonly List<EmployeeTimelineEntry> _added = [];

    public IReadOnlyList<EmployeeTimelineEntry> Added => _added;

    public Task<bool> TryAddAsync(EmployeeTimelineEntry entry, CancellationToken cancellationToken)
    {
        _added.Add(entry);
        return Task.FromResult(true);
    }
}
