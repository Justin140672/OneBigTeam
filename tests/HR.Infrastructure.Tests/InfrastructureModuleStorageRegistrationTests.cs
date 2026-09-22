using System.Collections.Generic;
using System.Linq;
using HR.Infrastructure;
using HR.Infrastructure.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.Internal;
using Xunit;

namespace HR.Infrastructure.Tests;

/// <summary>
/// Security review ticket 3 (P1): local (temp-directory) storage fallbacks for profile photos,
/// support attachments, and organisation exports must never be silently activated outside
/// Development/an explicit test environment — Staging/Production must fail fast at startup if the
/// corresponding Supabase configuration is missing.
/// </summary>
/// <remarks>
/// Shares the "InfrastructureModuleRegistration" collection with <see cref="EmailSenderRegistrationTests"/>:
/// that class briefly mutates the process-global E2E_TESTING environment variable, which
/// AddInfrastructure/AddEmailSender reads — running both classes in the same non-parallel
/// collection prevents that mutation from racing this class's own AddInfrastructure calls.
/// </remarks>
[Collection("InfrastructureModuleRegistration")]
public class InfrastructureModuleStorageRegistrationTests
{
    private const string ConnectionString =
        "Host=localhost;Database=hr_infrastructure_module_test_only;Username=none;Password=none";

    private static IConfiguration EmptyConfiguration() => new ConfigurationBuilder().Build();

    // Security review ticket 5 (P1): AddEmailSender now also fails closed for a fully-unconfigured
    // Staging/Production environment, and it runs before the storage registrations this test class
    // targets. Supply a fully-configured Postmark section (mirrors the shape already committed in
    // appsettings.json/appsettings.Staging.json) so these storage-focused tests keep isolating the
    // storage check specifically — see EmailSenderRegistrationTests for the email-specific coverage.
    private static IConfiguration WithFullPostmarkConfig(IConfiguration? baseConfiguration = null)
    {
        var builder = new ConfigurationBuilder();
        if (baseConfiguration is not null)
            builder.AddConfiguration(baseConfiguration);

        builder.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Infrastructure:Postmark:ServerToken"] = "test-server-token",
            ["Infrastructure:Postmark:FromEmail"] = "hello@example.com",
            ["Infrastructure:Postmark:MessageStream"] = "outbound",
            ["Infrastructure:Postmark:InvitationTemplateAlias"] = "user-invitation",
            ["Infrastructure:Postmark:PasswordResetTemplateAlias"] = "password-reset",
        });

        return builder.Build();
    }

    private static IServiceCollection BaseServices()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        return services;
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    public void NonDev_Without_ProfilePhoto_Config_Throws(string environmentName)
    {
        var services = BaseServices();
        var environment = new HostingEnvironment { EnvironmentName = environmentName };

        var exception = Record.Exception(() =>
            services.AddInfrastructure(ConnectionString, WithFullPostmarkConfig(), environment));

        Assert.IsType<InvalidOperationException>(exception);
        Assert.Contains("Profile photo storage", exception!.Message);
    }

    [Fact]
    public void Development_Without_Storage_Config_Registers_Local_Fallbacks()
    {
        var services = BaseServices();
        var environment = new HostingEnvironment { EnvironmentName = Environments.Development };

        services.AddInfrastructure(ConnectionString, EmptyConfiguration(), environment);

        Assert.Equal(
            "LocalProfilePhotoStorageService",
            services.Single(d => d.ServiceType == typeof(IProfilePhotoStorageService)).ImplementationType!.Name);
        Assert.Equal(
            "LocalSupportAttachmentStorageService",
            services.Single(d => d.ServiceType == typeof(ISupportAttachmentStorageService)).ImplementationType!.Name);
        Assert.Equal(
            "LocalOrganisationDataExportStorage",
            services.Single(d => d.ServiceType == typeof(IOrganisationDataExportStorage)).ImplementationType!.Name);
    }

    [Fact]
    public void Production_With_Full_Storage_Config_Registers_Supabase_Backed_Services()
    {
        var services = BaseServices();
        var environment = new HostingEnvironment { EnvironmentName = Environments.Production };

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Infrastructure:Supabase:ProfilePhotos:SupabaseUrl"] = "https://example.supabase.co",
                ["Infrastructure:Supabase:ProfilePhotos:ServiceRoleKey"] = "key",
                ["Infrastructure:Supabase:SupportAttachments:SupabaseUrl"] = "https://example.supabase.co",
                ["Infrastructure:Supabase:SupportAttachments:ServiceRoleKey"] = "key",
                ["Infrastructure:Supabase:OrganisationExports:SupabaseUrl"] = "https://example.supabase.co",
                ["Infrastructure:Supabase:OrganisationExports:ServiceRoleKey"] = "key",
            })
            .Build();

        var exception = Record.Exception(() =>
            services.AddInfrastructure(ConnectionString, WithFullPostmarkConfig(configuration), environment));

        Assert.Null(exception);
        Assert.DoesNotContain(services, d => d.ServiceType == typeof(IProfilePhotoStorageService)
            && d.ImplementationType?.Name == "LocalProfilePhotoStorageService");
        Assert.DoesNotContain(services, d => d.ServiceType == typeof(ISupportAttachmentStorageService)
            && d.ImplementationType?.Name == "LocalSupportAttachmentStorageService");
        Assert.DoesNotContain(services, d => d.ServiceType == typeof(IOrganisationDataExportStorage)
            && d.ImplementationType?.Name == "LocalOrganisationDataExportStorage");
    }
}
