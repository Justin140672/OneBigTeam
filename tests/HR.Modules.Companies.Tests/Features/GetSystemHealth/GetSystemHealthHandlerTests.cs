using HR.Modules.Companies.Features.GetSystemHealth;
using HR.Modules.Companies.Tests.Infrastructure;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace HR.Modules.Companies.Tests.Features.GetSystemHealth;

/// <summary>
/// HealthCheckService isn't practically mockable (it's a sealed framework implementation resolved
/// from DI), so these tests build a minimal ServiceCollection with fake IHealthCheck implementations
/// registered under the exact category keys the handler looks up ("database", "storage", "auth",
/// "email", "stripe", "hangfire") and resolve the real HealthCheckService from it, mirroring the
/// standard approach for testing code that depends on the ASP.NET Core health checks aggregator.
/// </summary>
public class GetSystemHealthHandlerTests
{
    [Fact]
    public async Task HandleAsync_Returns_Unauthorized_When_Email_Not_On_AllowList()
    {
        var healthCheckService = BuildHealthCheckService(registerAll: true);
        var handler = BuildHandler(
            new FakeCurrentUser(Guid.NewGuid(), email: "someone-else@example.com"),
            BuildConfiguration("admin@example.com"),
            healthCheckService);

        var result = await handler.HandleAsync(new GetSystemHealthRequest(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("unauthorized", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_Unauthorized_When_Email_Is_Null()
    {
        var healthCheckService = BuildHealthCheckService(registerAll: true);
        var handler = BuildHandler(
            new FakeCurrentUser(Guid.NewGuid(), email: null),
            BuildConfiguration("admin@example.com"),
            healthCheckService);

        var result = await handler.HandleAsync(new GetSystemHealthRequest(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("unauthorized", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_Mapped_Categories_For_AllowListed_Admin()
    {
        var healthCheckService = BuildHealthCheckService(registerAll: true);
        var handler = BuildHandler(
            new FakeCurrentUser(Guid.NewGuid(), email: "admin@example.com"),
            BuildConfiguration("admin@example.com"),
            healthCheckService);

        var result = await handler.HandleAsync(new GetSystemHealthRequest(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var response = result.Value!;

        Assert.False(string.IsNullOrWhiteSpace(response.OverallStatus));
        Assert.False(string.IsNullOrWhiteSpace(response.PlatformVersion));
        Assert.True(response.CheckedAt <= DateTimeOffset.UtcNow);
        Assert.True(response.CheckedAt > DateTimeOffset.UtcNow.AddMinutes(-1));

        Assert.Equal(6, response.Categories.Count);
        Assert.Equal(
            ["Database", "Storage", "Authentication", "Email", "Stripe", "Background Jobs"],
            response.Categories.Select(c => c.Name).ToArray());

        var database = response.Categories.Single(c => c.Name == "Database");
        Assert.Equal(HealthStatus.Healthy.ToString(), database.Status);
        Assert.Equal("db ok", database.Description);
    }

    [Fact]
    public async Task HandleAsync_Reports_Unregistered_Category_As_Unhealthy_With_Explanatory_Description()
    {
        // Register everything except "stripe" to exercise the missing-key branch.
        var healthCheckService = BuildHealthCheckService(registerAll: false);
        var handler = BuildHandler(
            new FakeCurrentUser(Guid.NewGuid(), email: "admin@example.com"),
            BuildConfiguration("admin@example.com"),
            healthCheckService);

        var result = await handler.HandleAsync(new GetSystemHealthRequest(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var stripe = result.Value!.Categories.Single(c => c.Name == "Stripe");

        Assert.Equal(HealthStatus.Unhealthy.ToString(), stripe.Status);
        Assert.Equal("No health check registered.", stripe.Description);
    }

    private static GetSystemHealthHandler BuildHandler(
        HR.SharedKernel.ICurrentUser currentUser,
        IConfiguration configuration,
        HealthCheckService healthCheckService)
    {
        return new GetSystemHealthHandler(healthCheckService, currentUser, configuration);
    }

    private static HealthCheckService BuildHealthCheckService(bool registerAll)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        var builder = services.AddHealthChecks();

        builder.AddCheck("database", () => HealthCheckResult.Healthy("db ok"));
        builder.AddCheck("storage", () => HealthCheckResult.Healthy("storage ok"));
        builder.AddCheck("auth", () => HealthCheckResult.Healthy("auth ok"));
        builder.AddCheck("email", () => HealthCheckResult.Degraded("email not configured"));
        builder.AddCheck("hangfire", () => HealthCheckResult.Healthy("hangfire ok"));

        if (registerAll)
        {
            builder.AddCheck("stripe", () => HealthCheckResult.Healthy("stripe ok"));
        }

        return services.BuildServiceProvider().GetRequiredService<HealthCheckService>();
    }

    private static IConfiguration BuildConfiguration(params string[] allowedEmails)
    {
        var configBuilder = new ConfigurationBuilder();

        if (allowedEmails.Length > 0)
        {
            var data = allowedEmails
                .Select((email, index) => new KeyValuePair<string, string?>($"PlatformAdmin:AllowedEmails:{index}", email))
                .ToArray();
            configBuilder.AddInMemoryCollection(data);
        }
        else
        {
            configBuilder.AddInMemoryCollection();
        }

        return configBuilder.Build();
    }
}
