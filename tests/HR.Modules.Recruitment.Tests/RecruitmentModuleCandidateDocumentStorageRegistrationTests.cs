using System.Collections.Generic;
using System.Linq;
using HR.Modules.Recruitment;
using HR.Modules.Recruitment.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.Internal;
using Xunit;

namespace HR.Modules.Recruitment.Tests;

/// <summary>
/// Reliability review issue 2 (P1): candidate document storage must never fall back to the
/// ephemeral local temp-directory implementation outside Development/an explicit automated-test
/// environment, and Staging/Production must fail fast at registration time rather than silently
/// using it when Supabase storage is not configured. Mirrors
/// HR.Modules.Documents.Tests.DocumentsModuleVirusScanRegistrationTests.
/// </summary>
public class RecruitmentModuleCandidateDocumentStorageRegistrationTests
{
    private const string ConnectionString =
        "Host=localhost;Database=hr_recruitment_module_test_only;Username=none;Password=none";

    private static IConfiguration EmptyConfiguration() => new ConfigurationBuilder().Build();

    private static IConfiguration ConfigurationWithSupabase() =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Recruitment:Supabase:CandidateDocuments:SupabaseUrl"] = "https://example.supabase.co",
                ["Recruitment:Supabase:CandidateDocuments:ServiceRoleKey"] = "service-role-key",
                ["Recruitment:Supabase:CandidateDocuments:BucketName"] = "candidate-documents",
            })
            .Build();

    [Fact]
    public void Production_Without_Supabase_Config_Throws_At_Registration()
    {
        var services = new ServiceCollection();
        var environment = new HostingEnvironment { EnvironmentName = Environments.Production };

        var exception = Record.Exception(() =>
            services.AddRecruitmentModule(ConnectionString, EmptyConfiguration(), environment));

        Assert.IsType<InvalidOperationException>(exception);
        Assert.Contains("Candidate document storage is not configured", exception!.Message);
    }

    [Fact]
    public void Staging_Without_Supabase_Config_Throws_At_Registration()
    {
        var services = new ServiceCollection();
        var environment = new HostingEnvironment { EnvironmentName = "Staging" };

        var exception = Record.Exception(() =>
            services.AddRecruitmentModule(ConnectionString, EmptyConfiguration(), environment));

        Assert.IsType<InvalidOperationException>(exception);
    }

    [Fact]
    public void Development_Without_Supabase_Config_Registers_Local_Storage()
    {
        var services = new ServiceCollection();
        var environment = new HostingEnvironment { EnvironmentName = Environments.Development };

        services.AddRecruitmentModule(ConnectionString, EmptyConfiguration(), environment);

        var descriptor = services.Single(d => d.ServiceType == typeof(ICandidateDocumentStorageService));
        Assert.Equal(typeof(LocalCandidateDocumentStorageService), descriptor.ImplementationType);
    }

    [Fact]
    public void Test_Environment_Without_Supabase_Config_Registers_Local_Storage()
    {
        var services = new ServiceCollection();
        var environment = new HostingEnvironment { EnvironmentName = "Test" };

        services.AddRecruitmentModule(ConnectionString, EmptyConfiguration(), environment);

        var descriptor = services.Single(d => d.ServiceType == typeof(ICandidateDocumentStorageService));
        Assert.Equal(typeof(LocalCandidateDocumentStorageService), descriptor.ImplementationType);
    }

    [Fact]
    public void Production_With_Supabase_Config_Registers_Durable_Storage_And_Health_Check()
    {
        var services = new ServiceCollection();
        var environment = new HostingEnvironment { EnvironmentName = Environments.Production };

        services.AddRecruitmentModule(ConnectionString, ConfigurationWithSupabase(), environment);

        // AddHttpClient<TInterface, TImpl> registers TInterface via a typed-client factory, not a
        // plain ImplementationType — resolve an instance to prove the concrete type actually wired.
        var provider = services.BuildServiceProvider();
        var resolved = provider.GetRequiredService<ICandidateDocumentStorageService>();
        Assert.IsType<SupabaseCandidateDocumentStorageService>(resolved);

        var options = provider.GetRequiredService<
            Microsoft.Extensions.Options.IOptions<Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckServiceOptions>>().Value;
        Assert.Contains(options.Registrations, r => r.Name == "candidate-document-storage");
    }

    [Fact]
    public void Staging_With_Supabase_Config_Registers_Durable_Storage()
    {
        var services = new ServiceCollection();
        var environment = new HostingEnvironment { EnvironmentName = "Staging" };

        services.AddRecruitmentModule(ConnectionString, ConfigurationWithSupabase(), environment);

        var provider = services.BuildServiceProvider();
        var resolved = provider.GetRequiredService<ICandidateDocumentStorageService>();
        Assert.IsType<SupabaseCandidateDocumentStorageService>(resolved);
    }

    [Fact]
    public void Development_With_Supabase_Config_Prefers_Durable_Storage()
    {
        var services = new ServiceCollection();
        var environment = new HostingEnvironment { EnvironmentName = Environments.Development };

        services.AddRecruitmentModule(ConnectionString, ConfigurationWithSupabase(), environment);

        var provider = services.BuildServiceProvider();
        var resolved = provider.GetRequiredService<ICandidateDocumentStorageService>();
        Assert.IsType<SupabaseCandidateDocumentStorageService>(resolved);
    }
}
