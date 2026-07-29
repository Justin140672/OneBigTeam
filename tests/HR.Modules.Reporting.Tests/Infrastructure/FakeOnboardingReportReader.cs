using HR.Infrastructure.Abstractions;

namespace HR.Modules.Reporting.Tests.Infrastructure;

/// <summary>
/// Hand-rolled fake for <see cref="IOnboardingReportReader"/> — records the arguments it was
/// called with (in particular, the employeeIds scoping filter used for row-level manager scoping)
/// and returns a pre-configured set of items, optionally filtered by the supplied employeeIds to
/// mimic the real reader's contract. Mirrors FakeProbationReportReader.
/// </summary>
internal sealed class FakeOnboardingReportReader : IOnboardingReportReader
{
    private readonly IReadOnlyList<OnboardingReportItem> _items;

    public FakeOnboardingReportReader(IReadOnlyList<OnboardingReportItem> items)
    {
        _items = items;
    }

    public Guid? LastCompanyId { get; private set; }
    public IReadOnlyCollection<Guid>? LastEmployeeIds { get; private set; }
    public bool WasCalled { get; private set; }

    public Task<IReadOnlyList<OnboardingReportItem>> GetOnboardingReportAsync(
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

        return Task.FromResult<IReadOnlyList<OnboardingReportItem>>(result);
    }
}
