using HR.Infrastructure.Abstractions;

namespace HR.Modules.Reporting.Tests.Infrastructure;

/// <summary>
/// Hand-rolled fake for <see cref="IProbationReportReader"/> — records the arguments it was
/// called with (in particular, the employeeIds scoping filter used for row-level manager scoping)
/// and returns a pre-configured set of items, optionally filtered by the supplied employeeIds to
/// mimic the real reader's contract.
/// </summary>
internal sealed class FakeProbationReportReader : IProbationReportReader
{
    private readonly IReadOnlyList<ProbationReportItem> _items;

    public FakeProbationReportReader(IReadOnlyList<ProbationReportItem> items)
    {
        _items = items;
    }

    public Guid? LastCompanyId { get; private set; }
    public IReadOnlyCollection<Guid>? LastEmployeeIds { get; private set; }
    public bool WasCalled { get; private set; }

    public Task<IReadOnlyList<ProbationReportItem>> GetProbationReportAsync(
        Guid companyId,
        IReadOnlyCollection<Guid>? employeeIds,
        CancellationToken cancellationToken)
    {
        LastCompanyId = companyId;
        LastEmployeeIds = employeeIds;
        WasCalled = true;

        var result = employeeIds is null
            ? _items
            : _items.Where(i => employeeIds.Contains(i.EmployeeId)).ToList();

        return Task.FromResult<IReadOnlyList<ProbationReportItem>>(result);
    }
}
