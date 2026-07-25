using HR.Infrastructure.Abstractions;
using HR.SharedKernel;

namespace HR.Modules.Employees.Services;

// Resolves "today" in the company's configured IANA/Windows time zone rather than assuming UTC.
// Falls back to UTC if the stored time zone id is missing or unrecognised by the host OS.
internal static class CompanyToday
{
    public static async Task<DateOnly> ResolveAsync(
        Guid companyId,
        IClock clock,
        ICompanyTimeZoneReader timeZoneReader,
        CancellationToken cancellationToken)
    {
        var timeZoneId = await timeZoneReader.GetTimeZoneAsync(companyId, cancellationToken);
        return clock.TodayIn(timeZoneId);
    }
}
