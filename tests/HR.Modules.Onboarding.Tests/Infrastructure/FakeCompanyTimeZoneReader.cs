using HR.Infrastructure.Abstractions;

namespace HR.Modules.Onboarding.Tests.Infrastructure;

internal sealed class FakeCompanyTimeZoneReader(string timeZoneId = "UTC") : ICompanyTimeZoneReader
{
    public Task<string> GetTimeZoneAsync(Guid companyId, CancellationToken cancellationToken) =>
        Task.FromResult(timeZoneId);
}
