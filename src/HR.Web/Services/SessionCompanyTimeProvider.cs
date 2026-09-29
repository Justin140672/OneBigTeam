namespace HR.Web.Services;

/// <summary>
/// Company-local date from the session's IANA time zone. No I/O, no caching: the zone is read from
/// <see cref="AppSession"/> (loaded at session start, refreshed with it). A missing, blank, unknown or
/// invalid zone falls back to UTC. The clock is injectable via <see cref="TimeProvider"/> for tests.
/// </summary>
public sealed class SessionCompanyTimeProvider(AppSession session, TimeProvider? timeProvider = null) : ICompanyTimeProvider
{
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

    public TimeZoneInfo TimeZone => ResolveTimeZone(session.TimeZone);

    public DateOnly Today => TodayIn(session.TimeZone, _timeProvider.GetUtcNow());

    public static TimeZoneInfo ResolveTimeZone(string? timeZoneId)
    {
        if (string.IsNullOrWhiteSpace(timeZoneId))
        {
            return TimeZoneInfo.Utc;
        }

        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException or ArgumentException)
        {
            return TimeZoneInfo.Utc;
        }
    }

    public static DateOnly TodayIn(string? timeZoneId, DateTimeOffset utcNow) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(utcNow, ResolveTimeZone(timeZoneId)).DateTime);
}
