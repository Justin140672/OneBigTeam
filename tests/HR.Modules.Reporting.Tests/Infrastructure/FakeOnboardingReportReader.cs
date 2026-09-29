using HR.Infrastructure.Abstractions;

namespace HR.Modules.Reporting.Tests.Infrastructure;

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
