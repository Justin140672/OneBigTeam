using HR.Infrastructure.Abstractions;
using HR.Modules.Reporting.Features.ExportProbationReport;
using HR.Modules.Reporting.Features.GetProbationReport;
using HR.Modules.Reporting.Tests.Infrastructure;

namespace HR.Modules.Reporting.Tests;

public class ExportProbationReportHandlerTests
{
    /// <summary>Throws from the reader call so the handler's catch block is exercised (REP-06).</summary>
    private sealed class ThrowingProbationReportReader : IProbationReportReader
    {
        public Task<IReadOnlyList<ProbationReportItem>> GetProbationReportAsync(
            Guid companyId, IReadOnlyCollection<Guid>? employeeIds, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("reader exploded");
    }

    private static ProbationReportItem BuildItem(Guid employeeId) =>
        new(
            employeeId,
            Guid.NewGuid(),
            "Active",
            new DateOnly(2026, 1, 1),
            new DateOnly(2026, 4, 1),
            DueReviewCount: 1,
            OverdueReviewCount: 2);

    [Fact]
    public async Task HandleAsync_Exports_Rows_From_GetHandler_Result()
    {
        var employeeId = Guid.NewGuid();
        var reader = new FakeProbationReportReader([BuildItem(employeeId)]);
        var getHandler = new GetProbationReportHandler(reader, new FakeEmployeeDepartmentReader(), new FakeDirectReportsReader());
        var exporter = new FakeReportExporter();
        var handler = new ExportProbationReportHandler(getHandler, exporter, TestReportExportAuditor.Create());

        var result = await handler.HandleAsync(
            new ExportProbationReportRequest(Guid.NewGuid()),
            callerIsHr: true,
            callerEmployeeId: Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Probation Report", exporter.LastData!.ReportTitle);
        Assert.Equal(
            ["Employee", "Status", "Start Date", "Expected End Date", "Due Reviews", "Overdue Reviews"],
            exporter.LastData.ColumnHeaders);
        var row = Assert.Single(exporter.LastData.Rows);
        Assert.Equal("Active", row[1]);
        Assert.Equal("2026-01-01", row[2]);
    }

    [Fact]
    public async Task HandleAsync_ManagerWithNoDirectReports_Exports_Empty_Rows()
    {
        var reader = new FakeProbationReportReader([BuildItem(Guid.NewGuid())]);
        var directReportsReader = new FakeDirectReportsReader([]);
        var getHandler = new GetProbationReportHandler(reader, new FakeEmployeeDepartmentReader(), directReportsReader);
        var exporter = new FakeReportExporter();
        var handler = new ExportProbationReportHandler(getHandler, exporter, TestReportExportAuditor.Create());

        var result = await handler.HandleAsync(
            new ExportProbationReportRequest(Guid.NewGuid()),
            callerIsHr: false,
            callerEmployeeId: Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(exporter.LastData!.Rows);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task HandleAsync_On_Success_Publishes_Audit_Event_With_ManagerScopeApplied_Reflecting_CallerIsHr(
        bool callerIsHr, bool expectedManagerScopeApplied)
    {
        var reader = new FakeProbationReportReader([BuildItem(Guid.NewGuid())]);
        var getHandler = new GetProbationReportHandler(reader, new FakeEmployeeDepartmentReader(), new FakeDirectReportsReader());
        var exporter = new FakeReportExporter();
        var auditor = TestReportExportAuditor.Create(out var publisher);
        var handler = new ExportProbationReportHandler(getHandler, exporter, auditor);

        var result = await handler.HandleAsync(
            new ExportProbationReportRequest(Guid.NewGuid()),
            callerIsHr,
            callerEmployeeId: Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        var evt = Assert.IsType<ReportExportAuditEvent>(Assert.Single(publisher.Published));
        Assert.True(evt.Success);
        Assert.Equal(expectedManagerScopeApplied, evt.ManagerScopeApplied);
    }

    [Fact]
    public async Task HandleAsync_On_Thrown_Exception_Publishes_Audit_Event_With_Success_False_And_Returns_Failure()
    {
        var reader = new ThrowingProbationReportReader();
        var getHandler = new GetProbationReportHandler(reader, new FakeEmployeeDepartmentReader(), new FakeDirectReportsReader());
        var exporter = new FakeReportExporter();
        var auditor = TestReportExportAuditor.Create(out var publisher);
        var handler = new ExportProbationReportHandler(getHandler, exporter, auditor);

        var result = await handler.HandleAsync(
            new ExportProbationReportRequest(Guid.NewGuid()),
            callerIsHr: true,
            callerEmployeeId: Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        var evt = Assert.IsType<ReportExportAuditEvent>(Assert.Single(publisher.Published));
        Assert.False(evt.Success);
        Assert.Equal("reader exploded", evt.FailureReason);
    }
}
