using HR.Infrastructure.Abstractions;

namespace HR.Modules.Employees.Tests.Infrastructure;

// DSH-05: small id-set fakes for the cross-module contract readers the manager team-status
// summary handler composes. Each records the ids/date it was asked about so tests can assert the
// handler only ever passes the counted-member id list (not the raw sub-tree).

internal sealed class FakeEmployeeLeaveStatusReader(params Guid[] onLeaveIds) : IEmployeeLeaveStatusReader
{
    public List<Guid>? LastRequestedIds { get; private set; }

    public Task<IReadOnlySet<Guid>> GetOnLeaveTodayEmployeeIdsAsync(
        Guid companyId, IEnumerable<Guid> employeeIds, CancellationToken cancellationToken)
    {
        var ids = employeeIds.ToList();
        LastRequestedIds = ids;
        return Task.FromResult<IReadOnlySet<Guid>>(onLeaveIds.Intersect(ids).ToHashSet());
    }
}

internal sealed class FakeEmployeesOffSickReader(params Guid[] sickIds) : IEmployeesOffSickReader
{
    public DateOnly? LastOnDate { get; private set; }
    public List<Guid>? LastRequestedIds { get; private set; }

    public Task<IReadOnlySet<Guid>> GetOffSickEmployeeIdsAsync(
        Guid companyId, IEnumerable<Guid> employeeIds, DateOnly onDate, CancellationToken cancellationToken)
    {
        var ids = employeeIds.ToList();
        LastOnDate = onDate;
        LastRequestedIds = ids;
        return Task.FromResult<IReadOnlySet<Guid>>(sickIds.Intersect(ids).ToHashSet());
    }
}

internal sealed class FakeEmployeesInProbationReader(params Guid[] inProbationIds) : IEmployeesInProbationReader
{
    public List<Guid>? LastRequestedIds { get; private set; }

    public Task<IReadOnlySet<Guid>> GetEmployeeIdsInProbationAsync(
        Guid companyId, IEnumerable<Guid> employeeIds, CancellationToken cancellationToken)
    {
        var ids = employeeIds.ToList();
        LastRequestedIds = ids;
        return Task.FromResult<IReadOnlySet<Guid>>(inProbationIds.Intersect(ids).ToHashSet());
    }
}

internal sealed class FakeEmployeesMissingFitNoteReader(params Guid[] missingIds) : IEmployeesMissingFitNoteReader
{
    public List<Guid>? LastRequestedIds { get; private set; }

    public Task<IReadOnlySet<Guid>> GetEmployeeIdsMissingFitNotesAsync(
        Guid companyId, IEnumerable<Guid> employeeIds, CancellationToken cancellationToken)
    {
        var ids = employeeIds.ToList();
        LastRequestedIds = ids;
        return Task.FromResult<IReadOnlySet<Guid>>(missingIds.Intersect(ids).ToHashSet());
    }
}
