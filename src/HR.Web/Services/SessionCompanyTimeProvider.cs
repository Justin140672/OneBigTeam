using HR.SharedKernel;

namespace HR.Web.Services;

public sealed class SessionCompanyTimeProvider(AppSession session) : ICompanyTimeProvider
{
    public TimeZoneInfo TimeZone
    {
        get
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(session.TimeZone);
            }
            catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException or ArgumentException)
            {
                return TimeZoneInfo.Utc;
            }
        }
    }

    public DateOnly Today =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, TimeZone).DateTime);

    public Task<DateOnly> GetTodayAsync(Guid companyId, CancellationToken cancellationToken = default) =>
        Task.FromResult(Today);
}
