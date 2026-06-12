using HR.Modules.Leave.Domain;

namespace HR.Modules.Leave.Services;

internal interface IPublicHolidayService
{
    Task<bool> IsPublicHoliday(DateOnly date);
    Task<IReadOnlyList<PublicHoliday>> GetPublicHolidays(DateOnly start, DateOnly end);
}
