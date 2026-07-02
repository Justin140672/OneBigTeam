using HR.SharedKernel;

namespace HR.Modules.Leave.Tests.Infrastructure;

internal sealed class FakePublicHolidayReader(IReadOnlyCollection<DateOnly>? holidays = null, string name = "") : IPublicHolidayReader
{
    private readonly IReadOnlyCollection<DateOnly> _holidays = holidays ?? [];
    private readonly string _name = name;

    public Task<IReadOnlyCollection<PublicHolidayDate>> GetPublicHolidaysAsync(
        Guid companyId,
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken)
    {
        IReadOnlyCollection<PublicHolidayDate> result = _holidays
            .Select(d => new PublicHolidayDate(d, _name))
            .ToList();
        return Task.FromResult(result);
    }
}
