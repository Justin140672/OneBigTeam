using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace HR.Modules.Companies.Persistence;

/// <summary>
/// Registered as the "database" named health check (System Health Dashboard, Platform Monitoring
/// epic). Deliberately just <see cref="Microsoft.EntityFrameworkCore.DatabaseFacade.CanConnectAsync"/>
/// on the Companies module's own DbContext rather than a cross-module/shared check — cheap (a single
/// round trip, no query), and since all modules share one Postgres instance, Companies' own
/// connectivity is a reliable proxy for overall database health without needing every module to
/// register its own identical check.
/// </summary>
internal sealed class CompaniesDatabaseHealthCheck(CompaniesDbContext dbContext) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var canConnect = await dbContext.Database.CanConnectAsync(cancellationToken);

            return canConnect
                ? HealthCheckResult.Healthy("Database connection succeeded.")
                : HealthCheckResult.Unhealthy("Database connection failed.");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("Database could not be reached.", ex);
        }
    }
}
