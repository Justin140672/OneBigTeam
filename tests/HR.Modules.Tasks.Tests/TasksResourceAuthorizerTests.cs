using HR.Modules.Tasks.Services;
using HR.Modules.Tasks.Tests.Infrastructure;

namespace HR.Modules.Tasks.Tests;

public class TasksResourceAuthorizerTests
{
    private static readonly Guid HrAdministratorRoleId = new("00000000-0000-0000-0000-000000000004");

    [Fact]
    public async Task CanAccessEmployeeTasksAsync_Allows_Self()
    {
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var authorizer = new TasksResourceAuthorizer(
            new FakeRoleAuthorizationService(), new FakeDirectReportsReader());

        var result = await authorizer.CanAccessEmployeeTasksAsync(
            companyId, employeeId, employeeId, CancellationToken.None);

        Assert.True(result);
    }

    [Fact]
    public async Task CanAccessEmployeeTasksAsync_Allows_HrAdministrator()
    {
        var companyId = Guid.NewGuid();
        var caller = Guid.NewGuid();
        var target = Guid.NewGuid();
        var authorizer = new TasksResourceAuthorizer(
            new FakeRoleAuthorizationService(HrAdministratorRoleId), new FakeDirectReportsReader());

        var result = await authorizer.CanAccessEmployeeTasksAsync(
            companyId, caller, target, CancellationToken.None);

        Assert.True(result);
    }

    [Fact]
    public async Task CanAccessEmployeeTasksAsync_Allows_Direct_Report()
    {
        var companyId = Guid.NewGuid();
        var manager = Guid.NewGuid();
        var report = Guid.NewGuid();
        var authorizer = new TasksResourceAuthorizer(
            new FakeRoleAuthorizationService(), new FakeDirectReportsReader(report));

        var result = await authorizer.CanAccessEmployeeTasksAsync(
            companyId, manager, report, CancellationToken.None);

        Assert.True(result);
    }

    [Fact]
    public async Task CanAccessEmployeeTasksAsync_Allows_Indirect_Skip_Level_Report()
    {
        var companyId = Guid.NewGuid();
        var skipLevelManager = Guid.NewGuid();
        var indirectReport = Guid.NewGuid();

        // GetAllDescendantIdsAsync returns the complete tree, so the fake models a skip-level
        // relationship the same way CompleteTaskHandlerTests does.
        var authorizer = new TasksResourceAuthorizer(
            new FakeRoleAuthorizationService(), new FakeDirectReportsReader(indirectReport));

        var result = await authorizer.CanAccessEmployeeTasksAsync(
            companyId, skipLevelManager, indirectReport, CancellationToken.None);

        Assert.True(result);
    }

    [Fact]
    public async Task CanAccessEmployeeTasksAsync_Denies_Unrelated_Peer()
    {
        var companyId = Guid.NewGuid();
        var caller = Guid.NewGuid();
        var target = Guid.NewGuid();
        var authorizer = new TasksResourceAuthorizer(
            new FakeRoleAuthorizationService(), new FakeDirectReportsReader());

        var result = await authorizer.CanAccessEmployeeTasksAsync(
            companyId, caller, target, CancellationToken.None);

        Assert.False(result);

        // Note: TasksResourceAuthorizer.CanAccessEmployeeTasksAsync always passes the same
        // companyId for both caller and target company sides (call-sites resolve the task's
        // company before calling), so cross-company denial isn't independently exercisable at
        // this layer — it's covered by the GetTaskEndpointTests/GetEmployeeTasksEndpointTests
        // integration tests, which route across two distinct seeded companies.
    }
}
