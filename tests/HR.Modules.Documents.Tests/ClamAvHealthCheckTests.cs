using HR.Modules.Documents.Services;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Xunit;

namespace HR.Modules.Documents.Tests;

/// <summary>
/// Security review ticket 2 (P1): readiness must report unhealthy when the configured ClamAv
/// daemon cannot be reached, rather than silently letting uploads accumulate against a scanner
/// that will never come back.
/// </summary>
public class ClamAvHealthCheckTests
{
    [Fact]
    public async Task Unreachable_Host_Reports_Unhealthy()
    {
        var options = Options.Create(new ClamAvOptions
        {
            Host = "192.0.2.1",
            Port = 3310,
            TimeoutSeconds = 1,
        });

        var check = new ClamAvHealthCheck(options);

        var result = await check.CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(HealthStatus.Unhealthy, result.Status);
    }
}
