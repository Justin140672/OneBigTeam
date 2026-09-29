using HR.Infrastructure.Abstractions;

namespace HR.Modules.Companies.Tests.Infrastructure;

internal sealed class FakePlatformUserActivityReader : IPlatformUserActivityReader
{
    public int TotalUserCountToReturn { get; set; }

    public Task<int> GetTotalUserCountAsync(CancellationToken cancellationToken) =>
        Task.FromResult(TotalUserCountToReturn);
}
