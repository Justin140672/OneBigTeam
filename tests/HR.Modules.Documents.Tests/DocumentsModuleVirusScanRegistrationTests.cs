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

    /// <summary>
    /// Reliability review issue 6: since issue 2 introduced the same fail-fast storage guard
    /// (AddStorageService) alongside the pre-existing malware-scanning guard, a Production/Staging
    /// registration test now needs BOTH a fully configured scanner AND fully configured document
    /// storage to succeed — a fixture with only ClamAv config throws on the storage check before
    /// ever reaching the scanner assertions.
    /// </summary>
    private static IConfiguration ConfigurationWithClamAvAndStorage() =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Documents:ClamAv:Host"] = "clamav.internal",
                ["Documents:ClamAv:Port"] = "3310",
                ["Documents:Supabase:SupabaseUrl"] = "https://example.supabase.co",
                ["Documents:Supabase:ServiceRoleKey"] = "service-role-key",
                ["Documents:Supabase:BucketName"] = "documents",
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

        // Requires storage config too (issue 2's fail-fast storage guard runs in the same
        // AddDocumentsModule call) — see ConfigurationWithClamAvAndStorage for why.
        services.AddDocumentsModule(ConnectionString, ConfigurationWithClamAvAndStorage(), environment);

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
    public void Production_With_Full_Config_Registers_Both_Real_Scanner_And_Durable_Storage()
    {
        // Reliability review issue 6: proves a fully-configured production module registers BOTH
        // real malware scanning AND durable (Supabase) document storage together — the two
        // fail-fast guards are independent but must both succeed for the module to start.
        var services = new ServiceCollection();
        var environment = new HostingEnvironment { EnvironmentName = Environments.Production };

        services.AddDocumentsModule(ConnectionString, ConfigurationWithClamAvAndStorage(), environment);

        var scannerDescriptor = services.Single(d => d.ServiceType == typeof(IVirusScanService));
        Assert.Equal(typeof(ClamAvVirusScanService), scannerDescriptor.ImplementationType);

        var provider = services.BuildServiceProvider();
        var storageService = provider.GetRequiredService<IDocumentStorageService>();
        Assert.IsType<SupabaseDocumentStorageService>(storageService);

        var options = provider.GetRequiredService<
            Microsoft.Extensions.Options.IOptions<Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckServiceOptions>>().Value;
        Assert.Contains(options.Registrations, r => r.Name == "clam-av");
        Assert.Contains(options.Registrations, r => r.Name == "document-storage");
    }

    [Fact]
    public void Production_With_ClamAv_Config_But_No_Storage_Config_Throws_At_Registration()
    {
        // Reliability review issue 6: the storage guard (issue 2) is independent of the scanner
        // guard — a fully configured scanner must not mask a missing storage configuration.
        var services = new ServiceCollection();
        var environment = new HostingEnvironment { EnvironmentName = Environments.Production };

        var exception = Record.Exception(() =>
            services.AddDocumentsModule(ConnectionString, ConfigurationWithClamAv(), environment));

        Assert.IsType<InvalidOperationException>(exception);
        Assert.Contains("Document storage is not configured", exception!.Message);
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

    [Fact]
    public void E2eTesting_With_ClamAv_Config_Still_Registers_NoOp_Scanner()
    {
        // HR.AppHost injects Documents__ClamAv__Host unconditionally, so an E2E run always has
        // ClamAv config — E2E_TESTING must still force the no-op scanner (the real scanner's job
        // can't download uploads from E2E storage and failed every scan, so nothing uploaded in an
        // E2E run ever became downloadable).
        var previous = System.Environment.GetEnvironmentVariable("E2E_TESTING");
        System.Environment.SetEnvironmentVariable("E2E_TESTING", "true");
        try
        {
            var services = new ServiceCollection();
            var environment = new HostingEnvironment { EnvironmentName = Environments.Development };

            services.AddDocumentsModule(ConnectionString, ConfigurationWithClamAv(), environment);

            var descriptor = services.Single(d => d.ServiceType == typeof(IVirusScanService));
            Assert.Equal(typeof(NoOpVirusScanService), descriptor.ImplementationType);
        }
        finally
        {
            System.Environment.SetEnvironmentVariable("E2E_TESTING", previous);
        }
    }
}
