using HR.Infrastructure.Abstractions;
using HR.Modules.Companies.Contracts;

namespace HR.Modules.Employees.Tests.Infrastructure;

internal sealed class FakeCompanyTimeZoneReader(string timeZoneId = "UTC") : ICompanyTimeZoneReader
{
    public Task<string> GetTimeZoneAsync(Guid companyId, CancellationToken cancellationToken) =>
        Task.FromResult(timeZoneId);
}

// Resolves a distinct time zone per company, for tests asserting that a job resolves "today"
// independently per company rather than once globally.
internal sealed class PerCompanyTimeZoneReader(IReadOnlyDictionary<Guid, string> timeZonesByCompanyId)
    : ICompanyTimeZoneReader
{
    public Task<string> GetTimeZoneAsync(Guid companyId, CancellationToken cancellationToken) =>
        Task.FromResult(timeZonesByCompanyId.TryGetValue(companyId, out var timeZoneId) ? timeZoneId : "UTC");
}
