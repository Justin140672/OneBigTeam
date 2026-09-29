using HR.Infrastructure.Abstractions;

namespace HR.Modules.Reporting.Tests.Infrastructure;

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
