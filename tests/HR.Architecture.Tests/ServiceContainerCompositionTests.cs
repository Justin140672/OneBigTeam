using HR.Modules.Tasks.Contracts;
using HR.Infrastructure;
using HR.Modules.Assets;
using HR.Modules.Companies;
using HR.Modules.DataImport;
using HR.Modules.Documents;
using HR.Modules.Employees;
using HR.Modules.Identity;
using HR.Modules.Leave;
using HR.Modules.Notifications;
using HR.Modules.Offboarding;
using HR.Modules.Onboarding;
using HR.Modules.Probation;
using HR.Modules.Recruitment;
using HR.Modules.Reporting;
using HR.Modules.Sickness;
using HR.Modules.Support;
using HR.Modules.Tasks;
using HR.SharedKernel;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.Internal;
using Microsoft.Extensions.Logging;

namespace HR.Architecture.Tests;

public class ServiceContainerCompositionTests
{
    [Fact]
    public void Composed_Container_Builds_Without_Circular_Or_Missing_Dependencies()
    {
        const string connectionString = "Host=localhost;Database=hr_container_validation_only;Username=none;Password=none";

        var configuration = new ConfigurationBuilder().Build();

        var services = new ServiceCollection();

        services.AddLogging();
        services.AddSingleton<IConfiguration>(configuration);

        services.AddAuthorizationCore();

        services.AddCompaniesModule(connectionString, configuration);
        var environment = new HostingEnvironment { EnvironmentName = Environments.Development };
        services.AddDataImportModule(connectionString, configuration, environment);
        services.AddDocumentsModule(connectionString, configuration, environment);
        services.AddEmployeesModule(connectionString);
        services.AddIdentityModule(connectionString, configuration);
        services.AddLeaveModule(connectionString);
        services.AddNotificationsModule(connectionString, configuration);
        services.AddOnboardingModule(connectionString);
        services.AddOffboardingModule(connectionString);
        services.AddTasksModule(connectionString);
        services.AddProbationModule(connectionString);
        services.AddRecruitmentModule(connectionString, configuration, environment);
        services.AddAssetsModule(connectionString);
        services.AddSicknessModule(connectionString);
        services.AddReportingModule(connectionString);
        // Security review ticket 4 (P1): Support's attachment handlers now depend on
        // IUploadedFileScanner, registered by AddDocumentsModule above — included here so this
        // test actually proves that cross-module DI wiring resolves.
        services.AddSupportModule(connectionString);
        services.AddInfrastructure(connectionString, configuration, environment);
        services.AddHangfireBackgroundJobs(connectionString);

        services.AddSingleton<IClock, SystemClock>();
        services.AddScoped<IIntegrationEventPublisher, IntegrationEventPublisher>();

        var exception = Record.Exception(() =>
        {
            using var provider = services.BuildServiceProvider(new ServiceProviderOptions
            {
                ValidateOnBuild = true,
                ValidateScopes = true,
            });
        });

        Assert.True(
            exception is null,
            $"The composed DI container failed to build. This means the application cannot start. " +
            $"Exception: {exception}");
    }
}
