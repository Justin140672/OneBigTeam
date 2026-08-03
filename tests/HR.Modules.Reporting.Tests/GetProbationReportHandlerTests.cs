using HR.Infrastructure.Abstractions;
using HR.Modules.Reporting.Features.GetProbationReport;
using HR.Modules.Reporting.Tests.Infrastructure;

namespace HR.Modules.Reporting.Tests;

public class GetProbationReportHandlerTests
{
    private static ProbationReportItem BuildItem(Guid employeeId, string status = "Active") =>
        new(
            employeeId,
            Guid.NewGuid(),
            status,
            new DateOnly(2026, 1, 1),
            new DateOnly(2026, 4, 1),
            DueReviewCount: 1,
            OverdueReviewCount: 2);

    [Fact]
    public async Task HandleAsync_HrCaller_Sees_CompanyWide_Data_Not_Scoped_To_DirectReports()
    {
        var employeeA = Guid.NewGuid();
        var employeeB = Guid.NewGuid();
        var reader = new FakeProbationReportReader([BuildItem(employeeA), BuildItem(employeeB)]);
        var directReportsReader = new FakeDirectReportsReader([employeeA]);
        var handler = new GetProbationReportHandler(reader, new FakeEmployeeDepartmentReader(), directReportsReader);

        var result = await handler.HandleAsync(
            new GetProbationReportRequest(Guid.NewGuid()),
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

        var reader = new FakeProbationReportReader([BuildItem(directReportId), BuildItem(otherEmployeeId)]);
        var directReportsReader = new FakeDirectReportsReader([directReportId]);
        var handler = new GetProbationReportHandler(reader, new FakeEmployeeDepartmentReader(), directReportsReader);

        var result = await handler.HandleAsync(
            new GetProbationReportRequest(companyId),
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
        var reader = new FakeProbationReportReader([BuildItem(Guid.NewGuid())]);
        var directReportsReader = new FakeDirectReportsReader([]);
        var handler = new GetProbationReportHandler(reader, new FakeEmployeeDepartmentReader(), directReportsReader);

        var result = await handler.HandleAsync(
            new GetProbationReportRequest(Guid.NewGuid()),
            callerIsHr: false,
            callerEmployeeId: Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value!.Items);
        Assert.Equal(0, result.Value.CurrentProbationCount);
        Assert.False(reader.WasCalled);
    }

    [Fact]
    public async Task HandleAsync_Excludes_Passed_Employees_From_Rows_But_Still_Counts_Them_In_PassedCount()
    {
        var activeEmployeeId = Guid.NewGuid();
        var passedEmployeeId = Guid.NewGuid();
        var reader = new FakeProbationReportReader(
        [
            BuildItem(activeEmployeeId, "Active"),
            BuildItem(passedEmployeeId, "Passed"),
        ]);
        var handler = new GetProbationReportHandler(reader, new FakeEmployeeDepartmentReader(), new FakeDirectReportsReader());

        var result = await handler.HandleAsync(
            new GetProbationReportRequest(Guid.NewGuid()), callerIsHr: true, callerEmployeeId: Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var row = Assert.Single(result.Value!.Items);
        Assert.Equal(activeEmployeeId, row.EmployeeId);
        Assert.DoesNotContain(result.Value.Items, i => i.EmployeeId == passedEmployeeId);
        Assert.Equal(1, result.Value.PassedCount);
    }

    [Fact]
    public async Task HandleAsync_Computes_Summary_Counts_From_Status()
    {
        var reader = new FakeProbationReportReader(
        [
            BuildItem(Guid.NewGuid(), "Active"),
            BuildItem(Guid.NewGuid(), "ReviewDue"),
            BuildItem(Guid.NewGuid(), "Passed"),
            BuildItem(Guid.NewGuid(), "Extended"),
        ]);
        var handler = new GetProbationReportHandler(reader, new FakeEmployeeDepartmentReader(), new FakeDirectReportsReader());

        var result = await handler.HandleAsync(
            new GetProbationReportRequest(Guid.NewGuid()), callerIsHr: true, callerEmployeeId: Guid.NewGuid(), CancellationToken.None);

        var response = result.Value!;
        Assert.Equal(2, response.CurrentProbationCount);
        Assert.Equal(1, response.PassedCount);
        Assert.Equal(1, response.ExtendedCount);
        Assert.Equal(4, response.DueReviewCount);
        Assert.Equal(8, response.OverdueReviewCount);
    }
}
