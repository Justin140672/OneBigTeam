using HR.SharedKernel;

namespace HR.Web.Services;

/// <summary>
/// HR.Web implementation of <see cref="ICompanyTimeProvider"/>. The server-side implementation lives
/// in HR.Infrastructure (needs the Companies DB) and is not available in the web host, so this one
/// derives the company-local date from the time zone already loaded into <see cref="AppSession"/>
/// (company settings). Falls back to UTC when the session has no/unknown time zone.
/// </summary>
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
