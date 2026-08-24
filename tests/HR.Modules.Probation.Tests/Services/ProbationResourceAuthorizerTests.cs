using HR.Modules.Probation.Services;
using HR.Modules.Probation.Tests.Infrastructure;

namespace HR.Modules.Probation.Tests.Services;

/// <summary>
/// PROB-02: unit coverage for ProbationResourceAuthorizer's HR-administrator bypass and
/// reporting-hierarchy resolution, mirroring HR.Modules.Leave.Tests.LeaveResourceAuthorizerTests /
/// HR.Modules.Sickness.Tests.Services.SicknessResourceAuthorizerTests' shape for the equivalent
/// LEAVE-02/SICK-02 authorizers.
/// </summary>
public class ProbationResourceAuthorizerTests
{
    // Mirrors HR.Modules.Probation.Services.ProbationResourceAuthorizer.HrAdministratorRoleId.
    private static readonly Guid HrAdministratorRoleId = new("00000000-0000-0000-0000-000000000004");
    private static readonly Guid ManagerRoleId = new("00000000-0000-0000-0000-000000000002");
    private static readonly Guid CompanyId = Guid.NewGuid();

    private static ProbationResourceAuthorizer BuildAuthorizer(
        FakeRoleAuthorizationService? authorizationService = null,
        FakeDirectReportsReader? directReportsReader = null) =>
        new(
            authorizationService ?? new FakeRoleAuthorizationService(),
            directReportsReader ?? new FakeDirectReportsReader());

    // ── IsHrAdministratorAsync ───────────────────────────────────────────────────

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
    public async Task IsHrAdministratorAsync_False_When_Only_Manager_Role_Present()
    {
        // Negated branch: a different role id (Manager) must not satisfy the HR Administrator
        // check.
        var caller = Guid.NewGuid();
        var authorizer = BuildAuthorizer(
            authorizationService: new FakeRoleAuthorizationService(ManagerRoleId));

        var result = await authorizer.IsHrAdministratorAsync(caller, CancellationToken.None);

        Assert.False(result);
    }

    [Fact]
    public async Task IsHrAdministratorAsync_False_When_No_Roles_Present()
    {
        var caller = Guid.NewGuid();
        var authorizer = BuildAuthorizer();

        var result = await authorizer.IsHrAdministratorAsync(caller, CancellationToken.None);

        Assert.False(result);
    }

    // ── GetAuthorizedEmployeeIdsAsync ────────────────────────────────────────────

    [Fact]
    public async Task GetAuthorizedEmployeeIdsAsync_Returns_Null_For_HrAdministrator()
    {
        var caller = Guid.NewGuid();
        var authorizer = BuildAuthorizer(
            authorizationService: new FakeRoleAuthorizationService(HrAdministratorRoleId));

        var result = await authorizer.GetAuthorizedEmployeeIdsAsync(CompanyId, caller, CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task GetAuthorizedEmployeeIdsAsync_Returns_Full_Hierarchy_For_Manager()
    {
        var manager = Guid.NewGuid();
        var directReport = Guid.NewGuid();
        var indirectReport = Guid.NewGuid();
        var authorizer = BuildAuthorizer(
            directReportsReader: new FakeDirectReportsReader(directReport, indirectReport));

        var result = await authorizer.GetAuthorizedEmployeeIdsAsync(CompanyId, manager, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(2, result!.Count);
        Assert.Contains(directReport, result);
        Assert.Contains(indirectReport, result);
    }

    [Fact]
    public async Task GetAuthorizedEmployeeIdsAsync_Returns_Empty_Set_For_Manager_With_No_Reports()
    {
        // Distinguishes "manager, but zero-length hierarchy" (empty set, callers should filter
        // to nothing) from the HR-administrator "unrestricted" null-sentinel case.
        var manager = Guid.NewGuid();
        var authorizer = BuildAuthorizer(directReportsReader: new FakeDirectReportsReader());

        var result = await authorizer.GetAuthorizedEmployeeIdsAsync(CompanyId, manager, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Empty(result!);
    }

    // ── CanViewEmployeeAsync ──────────────────────────────────────────────────────

    [Fact]
    public async Task CanViewEmployeeAsync_Allows_HrAdministrator_Regardless_Of_Hierarchy()
    {
        var caller = Guid.NewGuid();
        var target = Guid.NewGuid();
        var authorizer = BuildAuthorizer(
            authorizationService: new FakeRoleAuthorizationService(HrAdministratorRoleId),
            directReportsReader: new FakeDirectReportsReader()); // empty hierarchy

        var result = await authorizer.CanViewEmployeeAsync(CompanyId, caller, target, CancellationToken.None);

        Assert.True(result);
    }

    [Fact]
    public async Task CanViewEmployeeAsync_Allows_Direct_Manager()
    {
        var manager = Guid.NewGuid();
        var report = Guid.NewGuid();
        var authorizer = BuildAuthorizer(directReportsReader: new FakeDirectReportsReader(report));

        var result = await authorizer.CanViewEmployeeAsync(CompanyId, manager, report, CancellationToken.None);

        Assert.True(result);
    }

    [Fact]
    public async Task CanViewEmployeeAsync_Allows_Skip_Level_Manager_In_Three_Level_Hierarchy()
    {
        // GetAllDescendantIdsAsync is transitive — a skip-level manager's full descendant set
        // includes indirect reports, verified here by including the target directly in the
        // fake's returned set (matching how a real implementation would resolve it).
        var skipLevelManager = Guid.NewGuid();
        var indirectReport = Guid.NewGuid();
        var authorizer = BuildAuthorizer(directReportsReader: new FakeDirectReportsReader(indirectReport));

        var result = await authorizer.CanViewEmployeeAsync(
            CompanyId, skipLevelManager, indirectReport, CancellationToken.None);

        Assert.True(result);
    }

    [Fact]
    public async Task CanViewEmployeeAsync_Denies_Manager_When_Target_Not_In_Hierarchy()
    {
        var manager = Guid.NewGuid();
        var someoneElsesReport = Guid.NewGuid();
        var target = Guid.NewGuid();
        var authorizer = BuildAuthorizer(directReportsReader: new FakeDirectReportsReader(someoneElsesReport));

        var result = await authorizer.CanViewEmployeeAsync(CompanyId, manager, target, CancellationToken.None);

        Assert.False(result);
    }

    [Fact]
    public async Task CanViewEmployeeAsync_Denies_Own_Manager_Viewed_Bottom_Up()
    {
        // The hierarchy check is one-directional (manager sees reports, not vice versa) — being
        // someone's report does not grant view rights over the manager's own resources.
        var manager = Guid.NewGuid();
        var report = Guid.NewGuid();
        var authorizer = BuildAuthorizer(directReportsReader: new FakeDirectReportsReader(report));

        var result = await authorizer.CanViewEmployeeAsync(CompanyId, report, manager, CancellationToken.None);

        Assert.False(result);
    }
}
