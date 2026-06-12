using HR.Modules.Leave.Domain;

namespace HR.Modules.Leave.Services;

internal sealed class NullPublicHolidayService : IPublicHolidayService
{
    public Task<bool> IsPublicHoliday(DateOnly date) =>
        Task.FromResult(false);

    public Task<IReadOnlyList<PublicHoliday>> GetPublicHolidays(DateOnly start, DateOnly end) =>
        Task.FromResult<IReadOnlyList<PublicHoliday>>(Array.Empty<PublicHoliday>());
}
