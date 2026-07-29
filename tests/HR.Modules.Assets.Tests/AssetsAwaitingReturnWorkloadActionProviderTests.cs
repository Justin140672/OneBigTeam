using System.Security.Claims;
using HR.Infrastructure.Abstractions;
using HR.Modules.Assets.Services;
using HR.Modules.Assets.Tests.Infrastructure;

namespace HR.Modules.Assets.Tests;

/// <summary>
/// OBT-721 workload action provider tests for assets awaiting return. HR-only, filters to
/// unreturned ("Assigned") assignments only (see xmldoc on the provider for the interpretation
/// note on why every unreturned assignment counts as "awaiting return").
/// </summary>
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

        var result = await provider.GetActionsAsync(Guid.NewGuid(), AnyCaller(), CancellationToken.None);

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

        var result = await provider.GetActionsAsync(Guid.NewGuid(), AnyCaller(), CancellationToken.None);

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

        var result = await provider.GetActionsAsync(companyId, AnyCaller(), CancellationToken.None);

        var action = Assert.Single(result);
        Assert.Equal("Assets Awaiting Return", action.ActionCategory);
        Assert.Equal("Assigned - Not Yet Returned", action.Status);
        Assert.Null(action.DueDate);
        Assert.Equal($"/companies/{companyId}/assets/assignments/{assignmentId}/view", action.DeepLinkUrl);
    }
}
