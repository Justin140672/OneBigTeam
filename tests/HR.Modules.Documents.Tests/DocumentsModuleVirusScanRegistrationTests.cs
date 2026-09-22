using System.Collections.Generic;
using System.Linq;
using HR.Modules.Documents;
using HR.Modules.Documents.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.Internal;
using Xunit;

namespace HR.Modules.Documents.Tests;

/// <summary>
/// Security review ticket 2 (P1): the no-op virus scanner (which marks every upload "clean"
/// without inspecting it) must never be selectable outside Development/explicit-test
/// environments, and staging/production must fail fast at startup rather than silently falling
/// back to it when ClamAv is not configured.
/// </summary>
public class DocumentsModuleVirusScanRegistrationTests
{
    private const string ConnectionString =
        "Host=localhost;Database=hr_documents_module_test_only;Username=none;Password=none";

    private static IConfiguration EmptyConfiguration() => new ConfigurationBuilder().Build();

    private static IConfiguration ConfigurationWithClamAv() =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Documents:ClamAv:Host"] = "clamav.internal",
                ["Documents:ClamAv:Port"] = "3310",
            })
            .Build();

    [Fact]
    public void Production_Without_ClamAv_Config_Throws_At_Registration()
    {
        var services = new ServiceCollection();
        var environment = new HostingEnvironment { EnvironmentName = Environments.Production };

        var exception = Record.Exception(() =>
            services.AddDocumentsModule(ConnectionString, EmptyConfiguration(), environment));

        Assert.IsType<InvalidOperationException>(exception);
        Assert.Contains("Malware scanning is not configured", exception!.Message);
    }

    [Fact]
    public void Staging_Without_ClamAv_Config_Throws_At_Registration()
    {
        var services = new ServiceCollection();
        var environment = new HostingEnvironment { EnvironmentName = "Staging" };

        var exception = Record.Exception(() =>
            services.AddDocumentsModule(ConnectionString, EmptyConfiguration(), environment));

        Assert.IsType<InvalidOperationException>(exception);
    }

    [Fact]
    public void Development_Without_ClamAv_Config_Registers_NoOp_Scanner()
    {
        var services = new ServiceCollection();
        var environment = new HostingEnvironment { EnvironmentName = Environments.Development };

        services.AddDocumentsModule(ConnectionString, EmptyConfiguration(), environment);

        var descriptor = services.Single(d => d.ServiceType == typeof(IVirusScanService));
        Assert.Equal(typeof(NoOpVirusScanService), descriptor.ImplementationType);
    }

    [Fact]
    public void Test_Environment_Without_ClamAv_Config_Registers_NoOp_Scanner()
    {
        var services = new ServiceCollection();
        var environment = new HostingEnvironment { EnvironmentName = "Test" };

        services.AddDocumentsModule(ConnectionString, EmptyConfiguration(), environment);

        var descriptor = services.Single(d => d.ServiceType == typeof(IVirusScanService));
        Assert.Equal(typeof(NoOpVirusScanService), descriptor.ImplementationType);
    }

    [Fact]
    public void Production_With_ClamAv_Config_Registers_Real_Scanner_And_Health_Check()
    {
        var services = new ServiceCollection();
        var environment = new HostingEnvironment { EnvironmentName = Environments.Production };

        services.AddDocumentsModule(ConnectionString, ConfigurationWithClamAv(), environment);

        var scannerDescriptor = services.Single(d => d.ServiceType == typeof(IVirusScanService));
        Assert.Equal(typeof(ClamAvVirusScanService), scannerDescriptor.ImplementationType);

        Assert.Contains(services, d =>
            d.ServiceType.Name.Contains("HealthCheckService", StringComparison.Ordinal)
            || d.ServiceType == typeof(Microsoft.Extensions.Diagnostics.HealthChecks.IHealthCheck));

        // AddHealthChecks registers HealthCheckServiceOptions configuration; verify our specific
        // check name is present among the configured registrations.
        var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<
            Microsoft.Extensions.Options.IOptions<Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckServiceOptions>>().Value;
        Assert.Contains(options.Registrations, r => r.Name == "clam-av");
    }

    [Fact]
    public void Development_With_ClamAv_Config_Prefers_Real_Scanner()
    {
        var services = new ServiceCollection();
        var environment = new HostingEnvironment { EnvironmentName = Environments.Development };

        services.AddDocumentsModule(ConnectionString, ConfigurationWithClamAv(), environment);

        var descriptor = services.Single(d => d.ServiceType == typeof(IVirusScanService));
        Assert.Equal(typeof(ClamAvVirusScanService), descriptor.ImplementationType);
    }
}
