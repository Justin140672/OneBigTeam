namespace HR.Web.Services;

/// <summary>
/// Web-only, synchronous "company-local today" for Blazor components. Backed by the already-loaded
/// <see cref="AppSession"/> so it never performs I/O. Backend jobs/handlers must not use this; they
/// resolve the zone asynchronously via ICompanyTimeZoneReader and IClock.TodayIn(...).
/// </summary>
public interface ICompanyTimeProvider
{
    DateOnly Today { get; }

    TimeZoneInfo TimeZone { get; }
}
