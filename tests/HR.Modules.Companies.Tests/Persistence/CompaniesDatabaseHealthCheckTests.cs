using HR.Modules.Companies.Persistence;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace HR.Modules.Companies.Tests.Persistence;

public class CompaniesDatabaseHealthCheckTests
{
    [Fact]
    public async Task CheckHealthAsync_Returns_Healthy_When_Database_Can_Connect()
    {
        // The InMemory provider's CanConnectAsync always returns true, exercising the Healthy branch.
        var options = new DbContextOptionsBuilder<CompaniesDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        using var dbContext = new CompaniesDbContext(options);
        var healthCheck = new CompaniesDatabaseHealthCheck(dbContext);

        var result = await healthCheck.CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(HealthStatus.Healthy, result.Status);
    }
}
