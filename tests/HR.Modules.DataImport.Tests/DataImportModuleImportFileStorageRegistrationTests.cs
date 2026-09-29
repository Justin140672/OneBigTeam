using System.Collections.Generic;
using System.Linq;
using HR.Modules.DataImport;
using HR.Modules.DataImport.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.Internal;
using Xunit;

namespace HR.Modules.DataImport.Tests;

public class DataImportModuleImportFileStorageRegistrationTests
{
    private const string ConnectionString =
        "Host=localhost;Database=hr_data_import_module_test_only;Username=none;Password=none";

    private static IConfiguration EmptyConfiguration() => new ConfigurationBuilder().Build();

    private static IConfiguration ConfigurationWithSupabase() =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DataImport:Supabase:ImportFiles:SupabaseUrl"] = "https://example.supabase.co",
                ["DataImport:Supabase:ImportFiles:ServiceRoleKey"] = "service-role-key",
                ["DataImport:Supabase:ImportFiles:BucketName"] = "import-files",
            })
            .Build();

    [Fact]
    public void Production_Without_Supabase_Config_Throws_At_Registration()
    {
        var services = new ServiceCollection();
        var environment = new HostingEnvironment { EnvironmentName = Environments.Production };

        var exception = Record.Exception(() =>
            services.AddDataImportModule(ConnectionString, EmptyConfiguration(), environment));

        Assert.IsType<InvalidOperationException>(exception);
        Assert.Contains("Import file storage is not configured", exception!.Message);
    }

    [Fact]
    public void Staging_Without_Supabase_Config_Throws_At_Registration()
    {
        var services = new ServiceCollection();
        var environment = new HostingEnvironment { EnvironmentName = "Staging" };

        var exception = Record.Exception(() =>
            services.AddDataImportModule(ConnectionString, EmptyConfiguration(), environment));

        Assert.IsType<InvalidOperationException>(exception);
    }

    [Fact]
    public void Development_Without_Supabase_Config_Registers_Local_Storage()
    {
        var services = new ServiceCollection();
        var environment = new HostingEnvironment { EnvironmentName = Environments.Development };

        services.AddDataImportModule(ConnectionString, EmptyConfiguration(), environment);

        var descriptor = services.Single(d => d.ServiceType == typeof(IImportFileStorageService));
        Assert.Equal(typeof(LocalImportFileStorageService), descriptor.ImplementationType);
    }

    [Fact]
    public void Test_Environment_Without_Supabase_Config_Registers_Local_Storage()
    {
        var services = new ServiceCollection();
        var environment = new HostingEnvironment { EnvironmentName = "Test" };

        services.AddDataImportModule(ConnectionString, EmptyConfiguration(), environment);

        var descriptor = services.Single(d => d.ServiceType == typeof(IImportFileStorageService));
        Assert.Equal(typeof(LocalImportFileStorageService), descriptor.ImplementationType);
    }

    [Fact]
    public void Production_With_Supabase_Config_Registers_Durable_Storage_And_Health_Check()
    {
        var services = new ServiceCollection();
        var environment = new HostingEnvironment { EnvironmentName = Environments.Production };

        services.AddDataImportModule(ConnectionString, ConfigurationWithSupabase(), environment);

        var provider = services.BuildServiceProvider();
        var resolved = provider.GetRequiredService<IImportFileStorageService>();
        Assert.IsType<SupabaseImportFileStorageService>(resolved);

        var options = provider.GetRequiredService<
            Microsoft.Extensions.Options.IOptions<Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckServiceOptions>>().Value;
        Assert.Contains(options.Registrations, r => r.Name == "import-file-storage");
    }

    [Fact]
    public void Staging_With_Supabase_Config_Registers_Durable_Storage()
    {
        var services = new ServiceCollection();
        var environment = new HostingEnvironment { EnvironmentName = "Staging" };

        services.AddDataImportModule(ConnectionString, ConfigurationWithSupabase(), environment);

        var provider = services.BuildServiceProvider();
        var resolved = provider.GetRequiredService<IImportFileStorageService>();
        Assert.IsType<SupabaseImportFileStorageService>(resolved);
    }

    [Fact]
    public void Development_With_Supabase_Config_Prefers_Durable_Storage()
    {
        var services = new ServiceCollection();
        var environment = new HostingEnvironment { EnvironmentName = Environments.Development };

        services.AddDataImportModule(ConnectionString, ConfigurationWithSupabase(), environment);

        var provider = services.BuildServiceProvider();
        var resolved = provider.GetRequiredService<IImportFileStorageService>();
        Assert.IsType<SupabaseImportFileStorageService>(resolved);
    }
}
