using HR.Infrastructure.Abstractions;

namespace HR.Modules.Companies.Tests.Infrastructure;

/// <summary>
/// Minimal test double for <see cref="IPlatformUserActivityReader"/> — returns a
/// pre-configured platform-wide user count so GetApplicationMetricsHandler tests can
/// assert the mapped fields without a real HR.Modules.Identity dependency.
/// </summary>
internal sealed class FakePlatformUserActivityReader : IPlatformUserActivityReader
{
    public int TotalUserCountToReturn { get; set; }

    public Task<int> GetTotalUserCountAsync(CancellationToken cancellationToken) =>
        Task.FromResult(TotalUserCountToReturn);
}
