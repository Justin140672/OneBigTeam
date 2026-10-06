using HR.Modules.Identity.Features.SignUp;
using HR.Modules.Identity.Persistence;
using HR.Modules.Identity.Services;
using HR.SharedKernel;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace HR.Modules.Identity.Tests.Infrastructure;

internal sealed record Dependencies(
    FakeCompanyProvisioner Provisioner,
    FakeCompanyDefaultDataSeeder DefaultDataSeeder,
    FakeEmployeeProvisioningService EmployeeProvisioningService,
    FakeSupabaseAuthGateway SupabaseAuthGateway,
    FakeAuditEventPublisher AuditEventPublisher);

internal static class SignUpHandlerFactory
{
    public static SignUpHandler Build(
        IdentityDbContext dbContext, Dependencies dependencies, IClock clock, int inProgressWaitSeconds = 0)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["SignUp:InProgressWaitSeconds"] = inProgressWaitSeconds.ToString(),
            })
            .Build();

        var compensator = new SignUpOperationCompensator(
            dbContext,
            dependencies.Provisioner,
            dependencies.SupabaseAuthGateway,
            dependencies.AuditEventPublisher,
            clock,
            NullLogger<SignUpOperationCompensator>.Instance);

        return new SignUpHandler(
            dbContext,
            dependencies.Provisioner,
            dependencies.DefaultDataSeeder,
            dependencies.EmployeeProvisioningService,
            dependencies.SupabaseAuthGateway,
            dependencies.AuditEventPublisher,
            TestAccountCreationEmailGuard.Create(dependencies.AuditEventPublisher, clock),
            compensator,
            configuration,
            clock,
            NullLogger<SignUpHandler>.Instance);
    }

    public static Dependencies BuildDependencies() => new(
        new FakeCompanyProvisioner(),
        new FakeCompanyDefaultDataSeeder(),
        new FakeEmployeeProvisioningService(),
        new FakeSupabaseAuthGateway(),
        new FakeAuditEventPublisher());
}
