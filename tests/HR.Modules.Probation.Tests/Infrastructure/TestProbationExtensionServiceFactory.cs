using HR.Modules.Probation.Persistence;
using HR.Modules.Probation.Services;
using HR.SharedKernel;

namespace HR.Modules.Probation.Tests.Infrastructure;

internal static class TestProbationExtensionServiceFactory
{
    public static ProbationExtensionService Build(
        ProbationDbContext context,
        FakeTaskCreator? taskCreator = null,
        FakeTaskCanceller? taskCanceller = null,
        FakeEmployeeNameReader? employeeNameReader = null,
        FakeHrAdministratorDirectory? hrAdministratorDirectory = null,
        FakeNotificationWriter? notificationWriter = null,
        FakeAuditPublisher? auditPublisher = null,
        IIntegrationEventPublisher? integrationEventPublisher = null)
    {
        return new ProbationExtensionService(
            context,
            taskCreator ?? new FakeTaskCreator(),
            taskCanceller ?? new FakeTaskCanceller(),
            employeeNameReader ?? new FakeEmployeeNameReader(),
            hrAdministratorDirectory ?? new FakeHrAdministratorDirectory(),
            notificationWriter ?? new FakeNotificationWriter(),
            auditPublisher ?? new FakeAuditPublisher(),
            integrationEventPublisher ?? new NoOpIntegrationEventPublisher());
    }
}
