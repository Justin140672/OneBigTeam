using HR.Infrastructure;
using HR.Modules.Assets;
using HR.Modules.Companies;
using HR.Modules.DataImport;
using HR.Modules.Documents;
using HR.Modules.Employees;
using HR.Modules.Identity;
using HR.Modules.Leave;
using HR.Modules.Notifications;
using HR.Modules.Probation;
using HR.Modules.Recruitment;
using HR.Modules.Sickness;
using HR.Modules.Tasks;
using HR.SharedKernel;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace HR.Architecture.Tests;

/// <summary>
/// Composes the exact same set of module/infrastructure registrations that
/// <c>HR.Api/Program.cs</c> wires up, then asks the container to validate itself
/// (<see cref="ServiceProviderOptions.ValidateOnBuild"/> + <see cref="ServiceProviderOptions.ValidateScopes"/>).
///
/// This is the only test in the solution that actually proves the composed DI graph can be
/// built. Unit tests construct handlers manually with fakes and never assemble the real
/// container, so a constructor-time cycle across two modules (e.g. Recruitment and Tasks each
/// depending on the other through ITaskCompleter / IInterviewFeedbackService) can pass every
/// unit test while still crashing the application at startup. If this test ever fails, do not
/// work around it by disabling validation — it means the application cannot start.
///
/// No database connection is required: registration alone (AddDbContext, AddHangfire, etc.)
/// does not open a connection, and this test never resolves a scope or calls any Migrate/Seed
/// method.
/// </summary>
public class ServiceContainerCompositionTests
{
    [Fact]
    public void Composed_Container_Builds_Without_Circular_Or_Missing_Dependencies()
    {
        const string connectionString = "Host=localhost;Database=hr_container_validation_only;Username=none;Password=none";

        var configuration = new ConfigurationBuilder().Build();

        var services = new ServiceCollection();

        // WebApplicationBuilder normally supplies these; a plain ServiceCollection needs them
        // registered explicitly so that unrelated "missing ILogger<T>/IConfiguration" noise
        // does not mask (or get confused with) a genuine circular-dependency failure below.
        services.AddLogging();
        services.AddSingleton<IConfiguration>(configuration);

        services.AddCompaniesModule(connectionString);
        services.AddDataImportModule(connectionString, configuration);
        services.AddDocumentsModule(connectionString, configuration);
        services.AddEmployeesModule(connectionString);
        services.AddIdentityModule(connectionString);
        services.AddLeaveModule(connectionString);
        services.AddNotificationsModule(connectionString);
        services.AddTasksModule(connectionString);
        services.AddProbationModule(connectionString);
        services.AddRecruitmentModule(connectionString, configuration);
        services.AddAssetsModule(connectionString);
        services.AddSicknessModule(connectionString);
        services.AddInfrastructure(connectionString);
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
