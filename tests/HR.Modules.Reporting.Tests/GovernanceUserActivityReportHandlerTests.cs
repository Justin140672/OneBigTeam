using HR.Infrastructure.Abstractions;
using HR.Modules.Reporting.Features.ExportGovernanceUserActivityReport;
using HR.Modules.Reporting.Features.GetGovernanceUserActivityReport;
using HR.Modules.Reporting.GovernanceReporting;
using HR.Modules.Reporting.ReportRegistry;
using HR.Modules.Reporting.Tests.Infrastructure;
using HR.SharedKernel;
using static HR.Modules.Reporting.Tests.Infrastructure.GovernanceAuditTestData;

namespace HR.Modules.Reporting.Tests;

public class GovernanceUserActivityReportHandlerTests
{
    private static readonly Guid CompanyId = Guid.NewGuid();

    private static GetGovernanceUserActivityReportHandler GetHandler(
        FakeAuditHistoryReader audit, FakeUserEmailDirectoryReader? emails = null) =>
        new(audit, emails ?? new FakeUserEmailDirectoryReader());

    private static ExportGovernanceUserActivityReportHandler ExportHandler(
        FakeAuditHistoryReader audit, FakeReportExporter exporter, FakeUserEmailDirectoryReader? emails = null) =>
        new(audit, emails ?? new FakeUserEmailDirectoryReader(), exporter, TestReportExportAuditor.Create());

    // ── scope filtering ────────────────────────────────────────────────────

    [Fact]
    public async Task Get_Keeps_Only_Rows_With_An_Actor()
    {
        var actor = Guid.NewGuid();
        var audit = new FakeAuditHistoryReader(
        [
            Entry("employee.updated", actorUserId: actor),
            Entry("system.recalculated", actorUserId: null),
        ]);

        var result = await GetHandler(audit).HandleAsync(
            new GetGovernanceUserActivityReportRequest(CompanyId), CancellationToken.None);

        var row = Assert.Single(result.Value!.Items);
        Assert.Equal(actor, row.ActorUserId);
        Assert.Equal(1, result.Value.TotalCount);
    }

    // ── filters ────────────────────────────────────────────────────────────

    [Fact]
    public async Task Get_Actor_Filter_Narrows_To_One_Actor()
    {
        var wanted = Guid.NewGuid();
        var other = Guid.NewGuid();
        var audit = new FakeAuditHistoryReader(
        [
            Entry("a.b", actorUserId: wanted),
            Entry("a.b", actorUserId: other),
        ]);

        var result = await GetHandler(audit).HandleAsync(
            new GetGovernanceUserActivityReportRequest(CompanyId, ActorUserId: wanted), CancellationToken.None);

        Assert.All(result.Value!.Items, r => Assert.Equal(wanted, r.ActorUserId));
        Assert.Equal(1, result.Value.TotalCount);
    }

    [Fact]
    public async Task Get_Passes_EventType_EmployeeId_And_DateWindow_To_The_Reader()
    {
        var audit = new FakeAuditHistoryReader([]);
        var employeeId = Guid.NewGuid();

        await GetHandler(audit).HandleAsync(
            new GetGovernanceUserActivityReportRequest(
                CompanyId,
                EventType: "employee.updated",
                EmployeeId: employeeId,
                FromDate: new DateOnly(2026, 1, 1),
                ToDate: new DateOnly(2026, 1, 31)),
            CancellationToken.None);

        Assert.Equal(CompanyId, audit.LastCompanyId);
        Assert.Equal(employeeId, audit.LastEmployeeId);
        Assert.Equal("employee.updated", audit.LastEventType);
        Assert.Equal(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero), audit.LastFromDate);
        Assert.Equal(new DateTimeOffset(2026, 1, 31, 23, 59, 59, TimeSpan.Zero), audit.LastToDate);
        Assert.Equal(1, audit.LastPagination!.PageNumber);
        Assert.Equal(ReportLimits.ExportRowLimit, audit.LastPagination.PageSize);
    }

    [Theory]
    [InlineData("Failed", "login.failed")]
    [InlineData("Success", "login.succeeded")]
    public async Task Get_Status_Filter_Narrows_By_Derived_Status(string status, string expectedEvent)
    {
        var actor = Guid.NewGuid();
        var audit = new FakeAuditHistoryReader(
        [
            Entry("login.failed", actorUserId: actor),
            Entry("login.succeeded", actorUserId: actor),
        ]);

        var result = await GetHandler(audit).HandleAsync(
            new GetGovernanceUserActivityReportRequest(CompanyId, Status: status), CancellationToken.None);

        var row = Assert.Single(result.Value!.Items);
        Assert.Equal(expectedEvent, row.EventType);
    }

    // ── pagination ─────────────────────────────────────────────────────────

    [Fact]
    public async Task Get_Pages_In_Memory_And_Reports_Full_Filtered_TotalCount()
    {
        var actor = Guid.NewGuid();
        var entries = Enumerable.Range(0, 25)
            .Select(i => Entry("a.b", actorUserId: actor,
                occurredAt: new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero).AddMinutes(i)))
            .ToList();
        var audit = new FakeAuditHistoryReader(entries);

        var result = await GetHandler(audit).HandleAsync(
            new GetGovernanceUserActivityReportRequest(CompanyId, Page: 2, PageSize: 10), CancellationToken.None);

        Assert.Equal(25, result.Value!.TotalCount);
        Assert.Equal(10, result.Value.Items.Count);
        Assert.Equal(2, result.Value.Page);
    }

    [Fact]
    public async Task Get_Orders_Newest_First()
    {
        var actor = Guid.NewGuid();
        var older = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var newer = new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);
        var audit = new FakeAuditHistoryReader(
        [
            Entry("a.b", actorUserId: actor, occurredAt: older),
            Entry("a.c", actorUserId: actor, occurredAt: newer),
        ]);

        var result = await GetHandler(audit).HandleAsync(
            new GetGovernanceUserActivityReportRequest(CompanyId), CancellationToken.None);

        Assert.Equal(newer, result.Value!.Items[0].OccurredAt);
    }

    // ── actor email resolution ─────────────────────────────────────────────

    [Fact]
    public async Task Get_Resolves_Actor_Email()
    {
        var actor = Guid.NewGuid();
        var audit = new FakeAuditHistoryReader([Entry("a.b", actorUserId: actor)]);
        var emails = new FakeUserEmailDirectoryReader(new Dictionary<Guid, string> { [actor] = "amy@example.com" });

        var result = await GetHandler(audit, emails).HandleAsync(
            new GetGovernanceUserActivityReportRequest(CompanyId), CancellationToken.None);

        Assert.Equal("amy@example.com", Assert.Single(result.Value!.Items).ActorEmail);
    }

    [Fact]
    public async Task Get_Leaves_ActorEmail_Null_When_Directory_Has_No_Match()
    {
        var actor = Guid.NewGuid();
        var audit = new FakeAuditHistoryReader([Entry("a.b", actorUserId: actor)]);

        var result = await GetHandler(audit).HandleAsync(
            new GetGovernanceUserActivityReportRequest(CompanyId), CancellationToken.None);

        Assert.Null(Assert.Single(result.Value!.Items).ActorEmail);
    }

    // ── truncation ─────────────────────────────────────────────────────────

    [Fact]
    public async Task Get_IsTruncated_When_Underlying_Audit_Page_Hits_The_Cap()
    {
        var actor = Guid.NewGuid();
        var audit = new FakeAuditHistoryReader(
            [Entry("a.b", actorUserId: actor)], totalCount: ReportLimits.ExportRowLimit);

        var result = await GetHandler(audit).HandleAsync(
            new GetGovernanceUserActivityReportRequest(CompanyId), CancellationToken.None);

        Assert.True(result.Value!.IsTruncated);
    }

    [Fact]
    public async Task Get_Not_Truncated_Below_The_Cap()
    {
        var actor = Guid.NewGuid();
        var audit = new FakeAuditHistoryReader(
            [Entry("a.b", actorUserId: actor)], totalCount: ReportLimits.ExportRowLimit - 1);

        var result = await GetHandler(audit).HandleAsync(
            new GetGovernanceUserActivityReportRequest(CompanyId), CancellationToken.None);

        Assert.False(result.Value!.IsTruncated);
    }

    // ── export ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task Export_Builds_Governance_Column_Headers_And_Forwards_Format()
    {
        var actor = Guid.NewGuid();
        var audit = new FakeAuditHistoryReader([Entry("company.updated", actorUserId: actor)]);
        var exporter = new FakeReportExporter();

        await ExportHandler(audit, exporter).HandleAsync(
            new ExportGovernanceUserActivityReportRequest(CompanyId, Format: ReportExportFormat.Excel),
            CancellationToken.None);

        Assert.Equal(GovernanceAuditReportSupport.ColumnHeaders, exporter.LastData!.ColumnHeaders);
        Assert.Equal(ReportExportFormat.Excel, exporter.LastFormat);
    }

    [Fact]
    public async Task Export_Row_Set_Equals_The_Unpaged_Get_Result_For_Identical_Filters()
    {
        var actor = Guid.NewGuid();
        var entries = Enumerable.Range(0, 15)
            .Select(i => Entry("a.b", actorUserId: actor,
                occurredAt: new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero).AddMinutes(i),
                summary: $"s{i}"))
            .ToList();

        var getResult = await GetHandler(new FakeAuditHistoryReader(entries)).HandleAsync(
            new GetGovernanceUserActivityReportRequest(CompanyId, Page: 1, PageSize: 5), CancellationToken.None);

        var exporter = new FakeReportExporter();
        await ExportHandler(new FakeAuditHistoryReader(entries), exporter).HandleAsync(
            new ExportGovernanceUserActivityReportRequest(CompanyId), CancellationToken.None);

        Assert.Equal(15, getResult.Value!.TotalCount);
        Assert.Equal(15, exporter.LastData!.Rows.Count);
        // key cell: newest-first summary in column index 5
        Assert.Equal("s14", exporter.LastData.Rows[0][5]);
        Assert.Equal(getResult.Value.Items[0].Summary, exporter.LastData.Rows[0][5]);
    }
}
