using HR.Modules.Documents.Services;
using HR.Modules.Documents.Tests.Infrastructure;

namespace HR.Modules.Documents.Tests.Services;

public class DocumentResourceAuthorizerTests
{
    private static readonly Guid DocumentManagePermissionId = new("00000000-0000-0000-0001-000000000010");
    private static readonly Guid CompanyId = Guid.NewGuid();

    private static DocumentResourceAuthorizer BuildAuthorizer(
        FakePermissionAuthorizationService? authorizationService = null,
        FakeDirectReportsReader? directReportsReader = null) =>
        new(
            authorizationService ?? new FakePermissionAuthorizationService(),
            directReportsReader ?? new FakeDirectReportsReader());


    [Fact]
    public async Task IsHrAdministratorAsync_True_When_DocumentManage_Permission_Granted()
    {
        var caller = Guid.NewGuid();
        var authorizer = BuildAuthorizer(
            authorizationService: new FakePermissionAuthorizationService(DocumentManagePermissionId));

        var result = await authorizer.IsHrAdministratorAsync(caller, CancellationToken.None);

        Assert.True(result);
    }

    [Fact]
    public async Task IsHrAdministratorAsync_False_When_Permission_Not_Granted()
    {
        // Negated branch: a caller holding some other, unrelated permission id must not satisfy
        // the document.manage check.
        var caller = Guid.NewGuid();
        var otherPermissionId = Guid.NewGuid();
        var authorizer = BuildAuthorizer(
            authorizationService: new FakePermissionAuthorizationService(otherPermissionId));

        var result = await authorizer.IsHrAdministratorAsync(caller, CancellationToken.None);

        Assert.False(result);
    }

    [Fact]
    public async Task IsHrAdministratorAsync_False_When_No_Permissions_Granted()
    {
        var caller = Guid.NewGuid();
        var authorizer = BuildAuthorizer();

        var result = await authorizer.IsHrAdministratorAsync(caller, CancellationToken.None);

        Assert.False(result);
    }


    [Fact]
    public async Task CanAccessEmployeeDocumentsAsync_Allows_Self_Access()
    {
        var employee = Guid.NewGuid();
        var authorizer = BuildAuthorizer();

        var result = await authorizer.CanAccessEmployeeDocumentsAsync(
            CompanyId, employee, employee, CancellationToken.None);

        Assert.True(result);
    }

    [Fact]
    public async Task CanAccessEmployeeDocumentsAsync_Allows_HrAdministrator_For_Any_Target()
    {
        var caller = Guid.NewGuid();
        var target = Guid.NewGuid();
        var authorizer = BuildAuthorizer(
            authorizationService: new FakePermissionAuthorizationService(DocumentManagePermissionId),
            directReportsReader: new FakeDirectReportsReader()); // empty hierarchy — HR bypass must not depend on it

        var result = await authorizer.CanAccessEmployeeDocumentsAsync(
            CompanyId, caller, target, CancellationToken.None);

        Assert.True(result);
    }

    [Fact]
    public async Task CanAccessEmployeeDocumentsAsync_Allows_Direct_Manager_When_Target_In_Hierarchy()
    {
        var manager = Guid.NewGuid();
        var report = Guid.NewGuid();
        var authorizer = BuildAuthorizer(directReportsReader: new FakeDirectReportsReader(report));

        var result = await authorizer.CanAccessEmployeeDocumentsAsync(
            CompanyId, manager, report, CancellationToken.None);

        Assert.True(result);
    }

    [Fact]
    public async Task CanAccessEmployeeDocumentsAsync_Allows_Skip_Level_Manager_In_Three_Level_Hierarchy()
    {
        var skipLevelManager = Guid.NewGuid();
        var indirectReport = Guid.NewGuid();
        var authorizer = BuildAuthorizer(directReportsReader: new FakeDirectReportsReader(indirectReport));

        var result = await authorizer.CanAccessEmployeeDocumentsAsync(
            CompanyId, skipLevelManager, indirectReport, CancellationToken.None);

        Assert.True(result);
    }

    [Fact]
    public async Task CanAccessEmployeeDocumentsAsync_Denies_Manager_When_Target_Not_In_Hierarchy()
    {
        var manager = Guid.NewGuid();
        var someoneElsesReport = Guid.NewGuid();
        var target = Guid.NewGuid();
        var authorizer = BuildAuthorizer(directReportsReader: new FakeDirectReportsReader(someoneElsesReport));

        var result = await authorizer.CanAccessEmployeeDocumentsAsync(
            CompanyId, manager, target, CancellationToken.None);

        Assert.False(result);
    }

    [Fact]
    public async Task CanAccessEmployeeDocumentsAsync_Denies_Unrelated_Peer_Employee()
    {
        var caller = Guid.NewGuid();
        var target = Guid.NewGuid();
        var authorizer = BuildAuthorizer();

        var result = await authorizer.CanAccessEmployeeDocumentsAsync(
            CompanyId, caller, target, CancellationToken.None);

        Assert.False(result);
    }

    [Fact]
    public async Task CanAccessEmployeeDocumentsAsync_Denies_Own_Manager_Viewed_Bottom_Up()
    {
        var manager = Guid.NewGuid();
        var report = Guid.NewGuid();
        var authorizer = BuildAuthorizer(directReportsReader: new FakeDirectReportsReader(report));

        var result = await authorizer.CanAccessEmployeeDocumentsAsync(
            CompanyId, report, manager, CancellationToken.None);

        Assert.False(result);
    }
}
