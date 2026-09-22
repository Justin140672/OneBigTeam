using System.Collections.Generic;
using System.Linq;
using HR.Infrastructure;
using HR.Infrastructure.Email;
using HR.SharedKernel;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.Internal;
using Xunit;

namespace HR.Infrastructure.Tests;

/// <summary>
/// Security review ticket 5 (P1): the logging-only email senders (<see cref="LoggingEmailSender"/>
/// / <see cref="LoggingInvitationEmailSender"/> / <see cref="LoggingPasswordResetEmailSender"/>)
/// unconditionally report every send as delivered without ever contacting a real provider. That is
/// only acceptable in Development or an explicit automated-test environment — Staging/Production
/// must fail fast at startup when Postmark (server token, sender identity, message stream, or
/// template aliases) is not fully configured, exactly like the Ticket 2/3 fail-closed checks for
/// malware scanning and file storage.
/// </summary>
/// <remarks>
/// In the "InfrastructureModuleRegistration" collection (shared with
/// <see cref="InfrastructureModuleStorageRegistrationTests"/>) so this class's own brief mutation
/// of the process-global E2E_TESTING environment variable can never race another class's
/// AddInfrastructure/AddEmailSender call.
/// </remarks>
[Collection("InfrastructureModuleRegistration")]
public class EmailSenderRegistrationTests
{
    private const string ConnectionString =
        "Host=localhost;Database=hr_infrastructure_module_test_only;Username=none;Password=none";

    // Security review ticket 3 (P1) storage checks run after AddEmailSender but still execute
    // during AddInfrastructure — supply fully-configured storage so these email-focused tests
    // isolate the email check specifically (mirrors InfrastructureModuleStorageRegistrationTests'
    // own WithFullPostmarkConfig helper, done in reverse).
    private static IConfiguration WithFullStorageConfig(IConfiguration? baseConfiguration = null)
    {
        var builder = new ConfigurationBuilder();
        if (baseConfiguration is not null)
            builder.AddConfiguration(baseConfiguration);

        builder.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Infrastructure:Supabase:ProfilePhotos:SupabaseUrl"] = "https://example.supabase.co",
            ["Infrastructure:Supabase:ProfilePhotos:ServiceRoleKey"] = "key",
            ["Infrastructure:Supabase:SupportAttachments:SupabaseUrl"] = "https://example.supabase.co",
            ["Infrastructure:Supabase:SupportAttachments:ServiceRoleKey"] = "key",
            ["Infrastructure:Supabase:OrganisationExports:SupabaseUrl"] = "https://example.supabase.co",
            ["Infrastructure:Supabase:OrganisationExports:ServiceRoleKey"] = "key",
        });

        return builder.Build();
    }

    private static IConfiguration EmptyConfiguration() => WithFullStorageConfig();

    private static IConfiguration FullPostmarkConfiguration() => WithFullStorageConfig(
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Infrastructure:Postmark:ServerToken"] = "test-server-token",
                ["Infrastructure:Postmark:FromEmail"] = "hello@example.com",
                ["Infrastructure:Postmark:MessageStream"] = "outbound",
                ["Infrastructure:Postmark:InvitationTemplateAlias"] = "user-invitation",
                ["Infrastructure:Postmark:PasswordResetTemplateAlias"] = "password-reset",
            })
            .Build());

    private static IConfiguration PartialPostmarkConfiguration() => WithFullStorageConfig(
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                // Server token present, but the sender identity/message stream/template aliases
                // that make up the rest of "fully configured" are missing.
                ["Infrastructure:Postmark:ServerToken"] = "test-server-token",
            })
            .Build());

    private static IServiceCollection BaseServices()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        return services;
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    public void NonDev_Without_Any_Postmark_Config_Throws(string environmentName)
    {
        var services = BaseServices();
        var environment = new HostingEnvironment { EnvironmentName = environmentName };

        var exception = Record.Exception(() =>
            services.AddInfrastructure(ConnectionString, EmptyConfiguration(), environment));

        Assert.IsType<InvalidOperationException>(exception);
        Assert.Contains("Transactional email is not fully configured", exception!.Message);
        Assert.Contains("Infrastructure:Postmark:ServerToken", exception.Message);
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    public void NonDev_With_Partial_Postmark_Config_Throws_And_Lists_Missing_Settings(string environmentName)
    {
        var services = BaseServices();
        var environment = new HostingEnvironment { EnvironmentName = environmentName };

        var exception = Record.Exception(() =>
            services.AddInfrastructure(ConnectionString, PartialPostmarkConfiguration(), environment));

        Assert.IsType<InvalidOperationException>(exception);
        // ServerToken was supplied — must not be reported as missing — but the rest must be.
        Assert.DoesNotContain("Infrastructure:Postmark:ServerToken,", exception!.Message);
        Assert.Contains("Infrastructure:Postmark:FromEmail", exception.Message);
        Assert.Contains("Infrastructure:Postmark:MessageStream", exception.Message);
        Assert.Contains("Infrastructure:Postmark:InvitationTemplateAlias", exception.Message);
        Assert.Contains("Infrastructure:Postmark:PasswordResetTemplateAlias", exception.Message);
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    public void NonDev_With_Full_Postmark_Config_Registers_Real_Senders(string environmentName)
    {
        var services = BaseServices();
        var environment = new HostingEnvironment { EnvironmentName = environmentName };
        var configuration = FullPostmarkConfiguration();

        services.AddInfrastructure(ConnectionString, configuration, environment);
        // PostmarkInvitationEmailSender/PostmarkPasswordResetEmailSender resolve IConfiguration
        // directly (to read WebApp:BaseUrl) — AddInfrastructure doesn't register IConfiguration
        // itself (that's normally done by the host builder), so register it here.
        services.AddSingleton(configuration);

        // AddHttpClient<TInterface, TImplementation> registers a factory-backed ServiceDescriptor
        // (ImplementationType is null for these), so resolve the real instance type instead.
        using var provider = services.BuildServiceProvider();
        Assert.IsType<PostmarkEmailSender>(provider.GetRequiredService<IEmailSender>());
        Assert.IsType<PostmarkInvitationEmailSender>(provider.GetRequiredService<IInvitationEmailSender>());
        Assert.IsType<PostmarkPasswordResetEmailSender>(provider.GetRequiredService<IPasswordResetEmailSender>());
    }

    [Fact]
    public void Development_Without_Postmark_Config_Registers_Logging_Senders()
    {
        var services = BaseServices();
        var environment = new HostingEnvironment { EnvironmentName = Environments.Development };

        services.AddInfrastructure(ConnectionString, EmptyConfiguration(), environment);

        Assert.Equal(
            typeof(LoggingEmailSender),
            services.Single(d => d.ServiceType == typeof(IEmailSender)).ImplementationType);
        Assert.Equal(
            typeof(LoggingInvitationEmailSender),
            services.Single(d => d.ServiceType == typeof(IInvitationEmailSender)).ImplementationType);
        Assert.Equal(
            typeof(LoggingPasswordResetEmailSender),
            services.Single(d => d.ServiceType == typeof(IPasswordResetEmailSender)).ImplementationType);
    }

    [Fact]
    public void Test_Environment_Without_Postmark_Config_Registers_Logging_Senders()
    {
        var services = BaseServices();
        var environment = new HostingEnvironment { EnvironmentName = "Test" };

        services.AddInfrastructure(ConnectionString, EmptyConfiguration(), environment);

        Assert.Equal(
            typeof(LoggingEmailSender),
            services.Single(d => d.ServiceType == typeof(IEmailSender)).ImplementationType);
    }

    [Fact]
    public void E2eTesting_Flag_Forces_Logging_Senders_Even_With_Full_Postmark_Config()
    {
        var services = BaseServices();
        var environment = new HostingEnvironment { EnvironmentName = Environments.Development };

        Environment.SetEnvironmentVariable("E2E_TESTING", "true");
        try
        {
            services.AddInfrastructure(ConnectionString, FullPostmarkConfiguration(), environment);
        }
        finally
        {
            Environment.SetEnvironmentVariable("E2E_TESTING", null);
        }

        Assert.Equal(
            typeof(LoggingEmailSender),
            services.Single(d => d.ServiceType == typeof(IEmailSender)).ImplementationType);
    }

    [Fact]
    public void E2eTesting_Flag_In_NonDev_Environment_Still_Throws_Because_Logging_Senders_Are_Disallowed_There()
    {
        // E2E_TESTING is only ever legitimately set under Development (see HR.Api/Program.cs's own
        // startup guard); a non-Development environment must not treat it as license to fall back
        // to the logging stub.
        var services = BaseServices();
        var environment = new HostingEnvironment { EnvironmentName = Environments.Production };

        Environment.SetEnvironmentVariable("E2E_TESTING", "true");
        try
        {
            var exception = Record.Exception(() =>
                services.AddInfrastructure(ConnectionString, EmptyConfiguration(), environment));

            Assert.IsType<InvalidOperationException>(exception);
        }
        finally
        {
            Environment.SetEnvironmentVariable("E2E_TESTING", null);
        }
    }
}
