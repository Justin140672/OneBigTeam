using System.Net.Sockets;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace HR.Modules.Documents.Services;

/// <summary>
/// Security review ticket 2 (P1): readiness probe for the configured ClamAv daemon. Only
/// registered when ClamAv is actually configured (see DocumentsModule.AddStorageService) — a TCP
/// connect (no scan) is enough to prove the daemon is reachable without spending scan capacity on
/// every readiness poll. An unreachable/failed connect makes this instance not-ready (tagged
/// "critical") so uploads are never silently accepted with scanning unavailable.
/// </summary>
internal sealed class ClamAvHealthCheck(IOptions<ClamAvOptions> options) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var settings = options.Value;

        try
        {
            using var client = new TcpClient();
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(Math.Min(settings.TimeoutSeconds, 5)));

            await client.ConnectAsync(settings.Host, settings.Port, timeoutCts.Token);

            return HealthCheckResult.Healthy("ClamAv daemon reachable.");
        }
        catch (Exception ex)
        {
            // The raw exception (host/port/socket detail) is only ever exposed through the
            // HealthCheckResult.Exception property, which HealthCheckEndpoints deliberately never
            // serialises to the public response body — see its own remarks.
            return HealthCheckResult.Unhealthy("ClamAv daemon is not reachable.", ex);
        }
    }
}
