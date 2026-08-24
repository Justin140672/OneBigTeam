using HR.Modules.Probation.Persistence;
using HR.Modules.Probation.Services;

namespace HR.Modules.Probation.Tests.Infrastructure;

/// <summary>
/// Builds a ProbationExtensionService wired with in-memory fakes, for tests that exercise
/// CompleteProbationReviewHandler / CompleteProbationReviewFromTaskAction but do not care about
/// the extension side effects themselves (those get their own dedicated tests).
/// </summary>
internal static class TestProbationExtensionServiceFactory
{
    public static ProbationExtensionService Build(
        ProbationDbContext context,
        FakeTaskCreator? taskCreator = null,
        FakeTaskCanceller? taskCanceller = null,
        FakeEmployeeNameReader? employeeNameReader = null,
        FakeHrAdministratorDirectory? hrAdministratorDirectory = null,
        FakeNotificationWriter? notificationWriter = null,
        FakeAuditPublisher? auditPublisher = null)
    {
        return new ProbationExtensionService(
            context,
            taskCreator ?? new FakeTaskCreator(),
            taskCanceller ?? new FakeTaskCanceller(),
            employeeNameReader ?? new FakeEmployeeNameReader(),
            hrAdministratorDirectory ?? new FakeHrAdministratorDirectory(),
            notificationWriter ?? new FakeNotificationWriter(),
            auditPublisher ?? new FakeAuditPublisher());
    }
}
