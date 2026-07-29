using HR.Infrastructure.Abstractions;
using HR.Modules.Reporting.Features.GetOnboardingProgressReport;
using HR.Modules.Reporting.Tests.Infrastructure;

namespace HR.Modules.Reporting.Tests;

public class GetOnboardingProgressReportHandlerTests
{
    private static OnboardingReportItem BuildItem(
        Guid employeeId,
        int totalTasks = 4,
        int completedTasks = 2,
        IReadOnlyList<OnboardingReportTaskItem>? outstandingTasks = null) =>
        new(
            employeeId,
            Guid.NewGuid(),
            "InProgress",
            new DateOnly(2026, 1, 1),
            totalTasks,
            completedTasks,
            outstandingTasks ?? [new OnboardingReportTaskItem("Set up equipment", new DateOnly(2026, 2, 1), "IT", IsOverdue: false)]);

    [Fact]
    public async Task HandleAsync_HrCaller_Sees_CompanyWide_Data_Not_Scoped_To_DirectReports()
    {
        var employeeA = Guid.NewGuid();
        var employeeB = Guid.NewGuid();
        var reader = new FakeOnboardingReportReader([BuildItem(employeeA), BuildItem(employeeB)]);
        var directReportsReader = new FakeDirectReportsReader([employeeA]);
        var handler = new GetOnboardingProgressReportHandler(reader, new FakeEmployeeDepartmentReader(), directReportsReader);

        var result = await handler.HandleAsync(
            new GetOnboardingProgressReportRequest(Guid.NewGuid()),
            callerIsHr: true,
            callerEmployeeId: Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value!.Items.Count);
        Assert.Null(reader.LastEmployeeIds);
        Assert.Null(directReportsReader.LastManagerId);
    }

    [Fact]
    public async Task HandleAsync_ManagerCaller_Is_Scoped_To_DirectReports_Only()
    {
        var directReportId = Guid.NewGuid();
        var otherEmployeeId = Guid.NewGuid();
        var callerEmployeeId = Guid.NewGuid();
        var companyId = Guid.NewGuid();

        var reader = new FakeOnboardingReportReader([BuildItem(directReportId), BuildItem(otherEmployeeId)]);
        var directReportsReader = new FakeDirectReportsReader([directReportId]);
        var handler = new GetOnboardingProgressReportHandler(reader, new FakeEmployeeDepartmentReader(), directReportsReader);

        var result = await handler.HandleAsync(
            new GetOnboardingProgressReportRequest(companyId),
            callerIsHr: false,
            callerEmployeeId,
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        var row = Assert.Single(result.Value!.Items);
        Assert.Equal(directReportId, row.EmployeeId);
        Assert.Equal(companyId, directReportsReader.LastCompanyId);
        Assert.Equal(callerEmployeeId, directReportsReader.LastManagerId);
        Assert.NotNull(reader.LastEmployeeIds);
        Assert.Single(reader.LastEmployeeIds!);
    }

    [Fact]
    public async Task HandleAsync_ManagerWithNoDirectReports_Gets_Empty_Result_Without_Invoking_ReaderUnscoped()
    {
        var reader = new FakeOnboardingReportReader([BuildItem(Guid.NewGuid())]);
        var directReportsReader = new FakeDirectReportsReader([]);
        var handler = new GetOnboardingProgressReportHandler(reader, new FakeEmployeeDepartmentReader(), directReportsReader);

        var result = await handler.HandleAsync(
            new GetOnboardingProgressReportRequest(Guid.NewGuid()),
            callerIsHr: false,
            callerEmployeeId: Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value!.Items);
        Assert.Equal(0, result.Value.TotalEmployees);
        Assert.False(reader.WasCalled);
    }

    [Fact]
    public async Task HandleAsync_OverdueOnly_Excludes_Employees_With_No_Overdue_OutstandingTasks()
    {
        var overdueEmployeeId = Guid.NewGuid();
        var notOverdueEmployeeId = Guid.NewGuid();
        var reader = new FakeOnboardingReportReader(
        [
            BuildItem(overdueEmployeeId, outstandingTasks:
                [new OnboardingReportTaskItem("Late task", new DateOnly(2026, 1, 1), "IT", IsOverdue: true)]),
            BuildItem(notOverdueEmployeeId, outstandingTasks:
                [new OnboardingReportTaskItem("On time task", new DateOnly(2026, 6, 1), "IT", IsOverdue: false)]),
        ]);
        var handler = new GetOnboardingProgressReportHandler(reader, new FakeEmployeeDepartmentReader(), new FakeDirectReportsReader());

        var result = await handler.HandleAsync(
            new GetOnboardingProgressReportRequest(Guid.NewGuid(), OverdueOnly: true),
            callerIsHr: true,
            callerEmployeeId: Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        var row = Assert.Single(result.Value!.Items);
        Assert.Equal(overdueEmployeeId, row.EmployeeId);
    }

    [Fact]
    public async Task HandleAsync_Computes_ProgressPercent_From_Completed_Over_Total()
    {
        var employeeId = Guid.NewGuid();
        var reader = new FakeOnboardingReportReader([BuildItem(employeeId, totalTasks: 4, completedTasks: 3)]);
        var handler = new GetOnboardingProgressReportHandler(reader, new FakeEmployeeDepartmentReader(), new FakeDirectReportsReader());

        var result = await handler.HandleAsync(
            new GetOnboardingProgressReportRequest(Guid.NewGuid()),
            callerIsHr: true,
            callerEmployeeId: Guid.NewGuid(),
            CancellationToken.None);

        var row = Assert.Single(result.Value!.Items);
        Assert.Equal(75, row.ProgressPercent);
    }

    [Fact]
    public async Task HandleAsync_ProgressPercent_Is_Zero_When_TotalTasks_Is_Zero()
    {
        var employeeId = Guid.NewGuid();
        var reader = new FakeOnboardingReportReader([BuildItem(employeeId, totalTasks: 0, completedTasks: 0, outstandingTasks: [])]);
        var handler = new GetOnboardingProgressReportHandler(reader, new FakeEmployeeDepartmentReader(), new FakeDirectReportsReader());

        var result = await handler.HandleAsync(
            new GetOnboardingProgressReportRequest(Guid.NewGuid()),
            callerIsHr: true,
            callerEmployeeId: Guid.NewGuid(),
            CancellationToken.None);

        var row = Assert.Single(result.Value!.Items);
        Assert.Equal(0, row.ProgressPercent);
    }
}
