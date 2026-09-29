using HR.Infrastructure.Abstractions;
using HR.Modules.Companies.Contracts;

namespace HR.Modules.Employees.Tests.Infrastructure;

internal sealed class FakeCompanyTimeZoneReader(string timeZoneId = "UTC") : ICompanyTimeZoneReader
{
    public Task<string> GetTimeZoneAsync(Guid companyId, CancellationToken cancellationToken) =>
        Task.FromResult(timeZoneId);
}

internal sealed class PerCompanyTimeZoneReader(IReadOnlyDictionary<Guid, string> timeZonesByCompanyId)
    : ICompanyTimeZoneReader
{
    public Task<string> GetTimeZoneAsync(Guid companyId, CancellationToken cancellationToken) =>
        Task.FromResult(timeZonesByCompanyId.TryGetValue(companyId, out var timeZoneId) ? timeZoneId : "UTC");
}
