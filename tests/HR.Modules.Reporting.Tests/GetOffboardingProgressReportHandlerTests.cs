using HR.Infrastructure.Abstractions;
using HR.Modules.Reporting.Features.GetOffboardingProgressReport;
using HR.Modules.Reporting.Tests.Infrastructure;

namespace HR.Modules.Reporting.Tests;

public class GetOffboardingProgressReportHandlerTests
{
    private static OffboardingReportItem BuildItem(Guid employeeId, bool documentsReturned = true) =>
        new(
            employeeId,
            new DateOnly(2026, 8, 1),
            "InProgress",
            TotalTasks: 3,
            CompletedTasks: 1,
            OutstandingTaskTitles: ["Return laptop"],
            CompletedTaskTitles: ["Exit interview"],
            documentsReturned);

    [Fact]
    public async Task HandleAsync_Returns_All_Company_Employees_Regardless_Of_Caller_Role()
    {
        // GetOffboardingProgressReport has no row-level manager scoping — the endpoint policy is
        // reporting:view-hr only (HR Administrator), so unlike GetProbationReport/GetOnboarding-
        // ProgressReport the handler takes no callerIsHr/callerEmployeeId parameters at all.
        var employeeA = Guid.NewGuid();
        var employeeB = Guid.NewGuid();
        var reader = new FakeOffboardingReportReader([BuildItem(employeeA), BuildItem(employeeB)]);
        var handler = new GetOffboardingProgressReportHandler(
            reader,
            new FakeEmployeeDepartmentReader(),
            new FakeEmployeeUserAccountStatusReader(),
            new FakeAssignedAssetReader());

        var result = await handler.HandleAsync(
            new GetOffboardingProgressReportRequest(Guid.NewGuid()),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value!.Items.Count);
    }

    [Fact]
    public async Task HandleAsync_AccessDisabled_True_When_Account_Not_Active()
    {
        var employeeId = Guid.NewGuid();
        var reader = new FakeOffboardingReportReader([BuildItem(employeeId)]);
        var accountStatusReader = new FakeEmployeeUserAccountStatusReader();
        accountStatusReader.Statuses[employeeId] = new EmployeeUserAccountSummary(employeeId, EmployeeUserAccountStatus.Disabled, null);
        var handler = new GetOffboardingProgressReportHandler(
            reader, new FakeEmployeeDepartmentReader(), accountStatusReader, new FakeAssignedAssetReader());

        var result = await handler.HandleAsync(new GetOffboardingProgressReportRequest(Guid.NewGuid()), CancellationToken.None);

        var row = Assert.Single(result.Value!.Items);
        Assert.True(row.AccessDisabled);
    }

    [Fact]
    public async Task HandleAsync_AccessDisabled_True_When_No_Account_Exists()
    {
        var employeeId = Guid.NewGuid();
        var reader = new FakeOffboardingReportReader([BuildItem(employeeId)]);
        var handler = new GetOffboardingProgressReportHandler(
            reader, new FakeEmployeeDepartmentReader(), new FakeEmployeeUserAccountStatusReader(), new FakeAssignedAssetReader());

        var result = await handler.HandleAsync(new GetOffboardingProgressReportRequest(Guid.NewGuid()), CancellationToken.None);

        var row = Assert.Single(result.Value!.Items);
        Assert.True(row.AccessDisabled);
    }

    [Fact]
    public async Task HandleAsync_AccessDisabled_False_When_Account_Is_Active()
    {
        var employeeId = Guid.NewGuid();
        var reader = new FakeOffboardingReportReader([BuildItem(employeeId)]);
        var accountStatusReader = new FakeEmployeeUserAccountStatusReader();
        accountStatusReader.Statuses[employeeId] = new EmployeeUserAccountSummary(employeeId, EmployeeUserAccountStatus.Active, null);
        var handler = new GetOffboardingProgressReportHandler(
            reader, new FakeEmployeeDepartmentReader(), accountStatusReader, new FakeAssignedAssetReader());

        var result = await handler.HandleAsync(new GetOffboardingProgressReportRequest(Guid.NewGuid()), CancellationToken.None);

        var row = Assert.Single(result.Value!.Items);
        Assert.False(row.AccessDisabled);
    }

    [Fact]
    public async Task HandleAsync_DocumentsReturned_Passed_Through_From_Reader_Item()
    {
        var employeeId = Guid.NewGuid();
        var reader = new FakeOffboardingReportReader([BuildItem(employeeId, documentsReturned: false)]);
        var handler = new GetOffboardingProgressReportHandler(
            reader, new FakeEmployeeDepartmentReader(), new FakeEmployeeUserAccountStatusReader(), new FakeAssignedAssetReader());

        var result = await handler.HandleAsync(new GetOffboardingProgressReportRequest(Guid.NewGuid()), CancellationToken.None);

        var row = Assert.Single(result.Value!.Items);
        Assert.False(row.DocumentsReturned);
    }

    [Fact]
    public async Task HandleAsync_AssetsReturned_True_When_No_Assigned_Assets_Remain()
    {
        var returnedEmployeeId = Guid.NewGuid();
        var stillHoldingEmployeeId = Guid.NewGuid();
        var reader = new FakeOffboardingReportReader([BuildItem(returnedEmployeeId), BuildItem(stillHoldingEmployeeId)]);
        var assetReader = new FakeAssignedAssetReader(new Dictionary<Guid, IReadOnlyList<AssignedAssetItem>>
        {
            [stillHoldingEmployeeId] = [new AssignedAssetItem(Guid.NewGuid(), Guid.NewGuid(), "Laptop")],
        });
        var handler = new GetOffboardingProgressReportHandler(
            reader, new FakeEmployeeDepartmentReader(), new FakeEmployeeUserAccountStatusReader(), assetReader);

        var result = await handler.HandleAsync(new GetOffboardingProgressReportRequest(Guid.NewGuid()), CancellationToken.None);

        var returnedRow = result.Value!.Items.Single(r => r.EmployeeId == returnedEmployeeId);
        var stillHoldingRow = result.Value.Items.Single(r => r.EmployeeId == stillHoldingEmployeeId);
        Assert.True(returnedRow.AssetsReturned);
        Assert.False(stillHoldingRow.AssetsReturned);
        Assert.Equal(1, result.Value.OutstandingAssetsCount);
    }

    [Fact]
    public async Task HandleAsync_Returns_Empty_Response_When_No_Offboarding_Items()
    {
        var reader = new FakeOffboardingReportReader([]);
        var handler = new GetOffboardingProgressReportHandler(
            reader, new FakeEmployeeDepartmentReader(), new FakeEmployeeUserAccountStatusReader(), new FakeAssignedAssetReader());

        var result = await handler.HandleAsync(new GetOffboardingProgressReportRequest(Guid.NewGuid()), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value!.Items);
        Assert.Equal(0, result.Value.TotalEmployees);
    }
}
