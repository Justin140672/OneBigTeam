using HR.SharedKernel;

namespace HR.Modules.Sickness.Tests.Infrastructure;

internal sealed class FakePublicHolidayReader(IReadOnlyCollection<DateOnly>? holidays = null) : IPublicHolidayReader
{
    private readonly IReadOnlyCollection<DateOnly> _holidays = holidays ?? [];

    public Task<IReadOnlyCollection<PublicHolidayDate>> GetPublicHolidaysAsync(
        Guid companyId,
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken)
    {
        IReadOnlyCollection<PublicHolidayDate> result = _holidays
            .Select(d => new PublicHolidayDate(d, string.Empty))
            .ToList();
        return Task.FromResult(result);
    }
}
