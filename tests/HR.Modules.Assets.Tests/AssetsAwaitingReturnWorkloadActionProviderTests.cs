using System.Security.Claims;
using HR.Infrastructure.Abstractions;
using HR.Modules.Assets.Services;
using HR.Modules.Assets.Tests.Infrastructure;

namespace HR.Modules.Assets.Tests;

public class AssetsAwaitingReturnWorkloadActionProviderTests
{
    private static ClaimsPrincipal AnyCaller() => new(new ClaimsIdentity());

    private static AssetAssignmentReportItem BuildItem(
        Guid employeeId, string assetName, string returnStatus, Guid? assetAssignmentId = null) =>
        new(assetAssignmentId ?? Guid.NewGuid(), employeeId, assetName, "SN-001", DateTimeOffset.UtcNow, returnStatus);

    [Fact]
    public async Task HrCaller_Sees_Only_Unreturned_Assignments_CompanyWide()
    {
        var reader = new FakeAssetAssignmentReportReader(
        [
            BuildItem(Guid.NewGuid(), "Laptop", "Assigned"),
            BuildItem(Guid.NewGuid(), "Monitor", "Returned"),
        ]);

        var provider = new AssetsAwaitingReturnWorkloadActionProvider(
            reader, new FakeEmployeeDepartmentReader(), new FakeAuthorizationService("reporting:view-hr"));

        var result = await provider.GetActionsAsync(Guid.NewGuid(), AnyCaller(), WorkloadScope.Hr, CancellationToken.None);

        var action = Assert.Single(result);
        Assert.Equal("Return Laptop", action.ActionType);
    }

    [Fact]
    public async Task NonHrCaller_Returns_Empty_Not_Throws()
    {
        var reader = new FakeAssetAssignmentReportReader(
        [
            BuildItem(Guid.NewGuid(), "Laptop", "Assigned"),
        ]);

        var provider = new AssetsAwaitingReturnWorkloadActionProvider(
            reader, new FakeEmployeeDepartmentReader(), new FakeAuthorizationService());

        var result = await provider.GetActionsAsync(Guid.NewGuid(), AnyCaller(), WorkloadScope.Hr, CancellationToken.None);

        Assert.Empty(result);
    }

    [Fact]
    public async Task Maps_ActionCategory_Status_And_DeepLink()
    {
        var employeeId = Guid.NewGuid();
        var companyId = Guid.NewGuid();
        var assignmentId = Guid.NewGuid();
        var reader = new FakeAssetAssignmentReportReader(
        [
            BuildItem(employeeId, "Laptop", "Assigned", assignmentId),
        ]);

        var provider = new AssetsAwaitingReturnWorkloadActionProvider(
            reader, new FakeEmployeeDepartmentReader(), new FakeAuthorizationService("reporting:view-hr"));

        var result = await provider.GetActionsAsync(companyId, AnyCaller(), WorkloadScope.Hr, CancellationToken.None);

        var action = Assert.Single(result);
        Assert.Equal("Assets Awaiting Return", action.ActionCategory);
        Assert.Equal("Assigned - Not Yet Returned", action.Status);
        Assert.Null(action.DueDate);
        Assert.Equal($"/companies/{companyId}/employees/{employeeId}?tab=assets", action.DeepLinkUrl);
    }

    [Fact]
    public async Task HrCaller_Requesting_ManagerScope_Returns_Empty_HrOnly_Category_Never_Leaks_Into_Manager_Workspace()
    {
        var reader = new FakeAssetAssignmentReportReader(
        [
            BuildItem(Guid.NewGuid(), "Laptop", "Assigned"),
        ]);

        var provider = new AssetsAwaitingReturnWorkloadActionProvider(
            reader, new FakeEmployeeDepartmentReader(), new FakeAuthorizationService("reporting:view-hr"));

        var result = await provider.GetActionsAsync(Guid.NewGuid(), AnyCaller(), WorkloadScope.Manager, CancellationToken.None);

        Assert.Empty(result);
    }
}
