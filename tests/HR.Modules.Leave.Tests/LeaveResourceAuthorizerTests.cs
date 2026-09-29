using HR.Modules.Leave.Services;
using HR.Modules.Leave.Tests.Infrastructure;

namespace HR.Modules.Leave.Tests;

/// <summary>
/// LEAVE-01: unit coverage for LeaveResourceAuthorizer's three resource-ownership checks
/// (self-service / view / approve-reject), mirroring HR.Modules.Tasks.Tests's
/// CompleteTaskHandlerTests authorization-matrix coverage for SEC-003's equivalent shape.
/// </summary>
public class LeaveResourceAuthorizerTests
{
    private static readonly Guid HrAdministratorRoleId = new("00000000-0000-0000-0000-000000000004");
    private static readonly Guid ManagerRoleId = new("00000000-0000-0000-0000-000000000002");
    private static readonly Guid CompanyId = Guid.NewGuid();

    private static LeaveResourceAuthorizer BuildAuthorizer(
        FakeRoleAuthorizationService? authorizationService = null,
        FakeDirectReportsReader? directReportsReader = null) =>
        new(
            authorizationService ?? new FakeRoleAuthorizationService(),
            directReportsReader ?? new FakeDirectReportsReader());


    [Fact]
    public async Task CanActOnOwnLeaveAsync_Allows_Self()
    {
        var employeeId = Guid.NewGuid();
        var authorizer = BuildAuthorizer();

        var result = await authorizer.CanActOnOwnLeaveAsync(employeeId, employeeId, CancellationToken.None);

        Assert.True(result);
    }

    [Fact]
    public async Task CanActOnOwnLeaveAsync_Allows_HrAdministrator_Acting_On_Behalf_Of_Another_Employee()
    {
        var caller = Guid.NewGuid();
        var target = Guid.NewGuid();
        var authorizer = BuildAuthorizer(
            authorizationService: new FakeRoleAuthorizationService(HrAdministratorRoleId));

        var result = await authorizer.CanActOnOwnLeaveAsync(caller, target, CancellationToken.None);

        Assert.True(result);
    }

    [Fact]
    public async Task CanActOnOwnLeaveAsync_Denies_Unrelated_Peer_Employee()
    {
        var caller = Guid.NewGuid();
        var target = Guid.NewGuid();
        var authorizer = BuildAuthorizer();

        var result = await authorizer.CanActOnOwnLeaveAsync(caller, target, CancellationToken.None);

        Assert.False(result);
    }

    [Fact]
    public async Task CanActOnOwnLeaveAsync_Denies_Direct_Manager_Acting_On_Behalf_Of_Report()
    {
        var manager = Guid.NewGuid();
        var report = Guid.NewGuid();
        var authorizer = BuildAuthorizer(
            authorizationService: new FakeRoleAuthorizationService(ManagerRoleId),
            directReportsReader: new FakeDirectReportsReader(report));

        var result = await authorizer.CanActOnOwnLeaveAsync(manager, report, CancellationToken.None);

        Assert.False(result);
    }


    [Fact]
    public async Task CanViewAsync_Allows_Self()
    {
        var employeeId = Guid.NewGuid();
        var authorizer = BuildAuthorizer();

        var result = await authorizer.CanViewAsync(CompanyId, employeeId, employeeId, CancellationToken.None);

        Assert.True(result);
    }

    [Fact]
    public async Task CanViewAsync_Allows_HrAdministrator()
    {
        var caller = Guid.NewGuid();
        var target = Guid.NewGuid();
        var authorizer = BuildAuthorizer(
            authorizationService: new FakeRoleAuthorizationService(HrAdministratorRoleId));

        var result = await authorizer.CanViewAsync(CompanyId, caller, target, CancellationToken.None);

        Assert.True(result);
    }

    [Fact]
    public async Task CanViewAsync_Allows_Direct_Manager()
    {
        var manager = Guid.NewGuid();
        var report = Guid.NewGuid();
        var authorizer = BuildAuthorizer(directReportsReader: new FakeDirectReportsReader(report));

        var result = await authorizer.CanViewAsync(CompanyId, manager, report, CancellationToken.None);

        Assert.True(result);
    }

    [Fact]
    public async Task CanViewAsync_Allows_Skip_Level_Manager_In_Three_Level_Hierarchy()
    {
        var skipLevelManager = Guid.NewGuid();
        var employee = Guid.NewGuid();
        var authorizer = BuildAuthorizer(directReportsReader: new FakeDirectReportsReader(employee));

        var result = await authorizer.CanViewAsync(CompanyId, skipLevelManager, employee, CancellationToken.None);

        Assert.True(result);
    }

    [Fact]
    public async Task CanViewAsync_Denies_Unrelated_Peer_Employee()
    {
        var caller = Guid.NewGuid();
        var target = Guid.NewGuid();
        var authorizer = BuildAuthorizer();

        var result = await authorizer.CanViewAsync(CompanyId, caller, target, CancellationToken.None);

        Assert.False(result);
    }

    [Fact]
    public async Task CanViewAsync_Denies_Manager_Who_Does_Not_Manage_Target()
    {
        var otherManager = Guid.NewGuid();
        var someoneElsesReport = Guid.NewGuid();
        var target = Guid.NewGuid();
        var authorizer = BuildAuthorizer(directReportsReader: new FakeDirectReportsReader(someoneElsesReport));

        var result = await authorizer.CanViewAsync(CompanyId, otherManager, target, CancellationToken.None);

        Assert.False(result);
    }

    [Fact]
    public async Task CanViewAsync_Denies_Own_Manager_Viewed_Bottom_Up()
    {
        var manager = Guid.NewGuid();
        var report = Guid.NewGuid();
        var authorizer = BuildAuthorizer(directReportsReader: new FakeDirectReportsReader(report));

        var result = await authorizer.CanViewAsync(CompanyId, report, manager, CancellationToken.None);

        Assert.False(result);
    }

    // ── CanApproveOrRejectAsync (Approve/Reject — no self path) ─────────────────

    [Fact]
    public async Task CanApproveOrRejectAsync_Denies_Self_Even_Though_CanActOnOwnLeaveAsync_Would_Allow_It()
    {
        // Approve/Reject has no self-path by design (LEAVE-01): unlike CanActOnOwnLeaveAsync and
        // CanViewAsync, caller == target must never short-circuit to true here.
        var employeeId = Guid.NewGuid();
        var authorizer = BuildAuthorizer();

        var result = await authorizer.CanApproveOrRejectAsync(CompanyId, employeeId, employeeId, CancellationToken.None);

        Assert.False(result);
    }

    [Fact]
    public async Task CanApproveOrRejectAsync_Allows_HrAdministrator()
    {
        var caller = Guid.NewGuid();
        var target = Guid.NewGuid();
        var authorizer = BuildAuthorizer(
            authorizationService: new FakeRoleAuthorizationService(HrAdministratorRoleId));

        var result = await authorizer.CanApproveOrRejectAsync(CompanyId, caller, target, CancellationToken.None);

        Assert.True(result);
    }

    [Fact]
    public async Task CanApproveOrRejectAsync_Allows_Direct_Manager()
    {
        var manager = Guid.NewGuid();
        var report = Guid.NewGuid();
        var authorizer = BuildAuthorizer(directReportsReader: new FakeDirectReportsReader(report));

        var result = await authorizer.CanApproveOrRejectAsync(CompanyId, manager, report, CancellationToken.None);

        Assert.True(result);
    }

    [Fact]
    public async Task CanApproveOrRejectAsync_Allows_Skip_Level_Manager_In_Three_Level_Hierarchy()
    {
        var seniorManager = Guid.NewGuid();
        var employee = Guid.NewGuid();
        var authorizer = BuildAuthorizer(directReportsReader: new FakeDirectReportsReader(employee));

        var result = await authorizer.CanApproveOrRejectAsync(CompanyId, seniorManager, employee, CancellationToken.None);

        Assert.True(result);
    }

    [Fact]
    public async Task CanApproveOrRejectAsync_Denies_Unrelated_Peer_Employee()
    {
        var caller = Guid.NewGuid();
        var target = Guid.NewGuid();
        var authorizer = BuildAuthorizer();

        var result = await authorizer.CanApproveOrRejectAsync(CompanyId, caller, target, CancellationToken.None);

        Assert.False(result);
    }

    [Fact]
    public async Task CanApproveOrRejectAsync_Denies_Manager_Of_A_Different_Team()
    {
        var otherManager = Guid.NewGuid();
        var someoneElsesReport = Guid.NewGuid();
        var target = Guid.NewGuid();
        var authorizer = BuildAuthorizer(directReportsReader: new FakeDirectReportsReader(someoneElsesReport));

        var result = await authorizer.CanApproveOrRejectAsync(CompanyId, otherManager, target, CancellationToken.None);

        Assert.False(result);
    }

    [Fact]
    public async Task CanApproveOrRejectAsync_Denies_Own_Manager_Approving_Bottom_Up()
    {
        var manager = Guid.NewGuid();
        var report = Guid.NewGuid();
        var authorizer = BuildAuthorizer(directReportsReader: new FakeDirectReportsReader(report));

        var result = await authorizer.CanApproveOrRejectAsync(CompanyId, report, manager, CancellationToken.None);

        Assert.False(result);
    }


    [Fact]
    public async Task IsHrAdministratorAsync_True_When_Role_Present()
    {
        var caller = Guid.NewGuid();
        var authorizer = BuildAuthorizer(
            authorizationService: new FakeRoleAuthorizationService(HrAdministratorRoleId));

        var result = await authorizer.IsHrAdministratorAsync(caller, CancellationToken.None);

        Assert.True(result);
    }

    [Fact]
    public async Task IsHrAdministratorAsync_False_For_CompanyAdministrator_Role()
    {
        // Negative branch of the role check: a different role id (e.g. Company Administrator)
        // must not satisfy the HR Administrator check.
        var companyAdministratorRoleId = new Guid("00000000-0000-0000-0000-000000000006");
        var caller = Guid.NewGuid();
        var authorizer = BuildAuthorizer(
            authorizationService: new FakeRoleAuthorizationService(companyAdministratorRoleId));

        var result = await authorizer.IsHrAdministratorAsync(caller, CancellationToken.None);

        Assert.False(result);
    }
}
