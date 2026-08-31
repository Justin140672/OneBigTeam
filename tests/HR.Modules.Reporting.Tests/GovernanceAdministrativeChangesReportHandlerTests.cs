using HR.Infrastructure.Abstractions;
using HR.Modules.Reporting.Features.ExportGovernanceAdministrativeChangesReport;
using HR.Modules.Reporting.Features.GetGovernanceAdministrativeChangesReport;
using HR.Modules.Reporting.GovernanceReporting;
using HR.Modules.Reporting.ReportRegistry;
using HR.Modules.Reporting.Tests.Infrastructure;
using static HR.Modules.Reporting.Tests.Infrastructure.GovernanceAuditTestData;

namespace HR.Modules.Reporting.Tests;

public class GovernanceAdministrativeChangesReportHandlerTests
{
    private static readonly Guid CompanyId = Guid.NewGuid();

    private static GetGovernanceAdministrativeChangesReportHandler GetHandler(FakeAuditHistoryReader audit) =>
        new(audit, new FakeUserEmailDirectoryReader());

    [Fact]
    public async Task Get_Keeps_Only_Configuration_And_Policy_Change_Events()
    {
        var actor = Guid.NewGuid();
        var audit = new FakeAuditHistoryReader(
        [
            Entry("company.settings-updated", actorUserId: actor),
            Entry("leave-policy.updated", actorUserId: actor),
            Entry("role.permissions-changed", actorUserId: actor),
            Entry("employee.updated", actorUserId: actor),   // out of scope
            Entry("login.succeeded", actorUserId: actor),     // out of scope
        ]);

        var result = await GetHandler(audit).HandleAsync(
            new GetGovernanceAdministrativeChangesReportRequest(CompanyId), CancellationToken.None);

        Assert.Equal(3, result.Value!.TotalCount);
        Assert.All(result.Value.Items, r =>
            Assert.DoesNotContain(r.EventType, new[] { "employee.updated", "login.succeeded" }));
    }

    [Fact]
    public async Task Get_Includes_Rows_Without_An_Actor_When_Event_Type_Is_In_Scope()
    {
        var audit = new FakeAuditHistoryReader([Entry("company.settings-updated", actorUserId: null)]);

        var result = await GetHandler(audit).HandleAsync(
            new GetGovernanceAdministrativeChangesReportRequest(CompanyId), CancellationToken.None);

        Assert.Equal(1, result.Value!.TotalCount);
    }

    [Fact]
    public async Task Get_Pagination_Reports_Full_Filtered_Count()
    {
        var actor = Guid.NewGuid();
        var entries = Enumerable.Range(0, 12)
            .Select(i => Entry("settings.changed", actorUserId: actor,
                occurredAt: new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero).AddMinutes(i)))
            .ToList();

        var result = await GetHandler(new FakeAuditHistoryReader(entries)).HandleAsync(
            new GetGovernanceAdministrativeChangesReportRequest(CompanyId, Page: 2, PageSize: 5),
            CancellationToken.None);

        Assert.Equal(12, result.Value!.TotalCount);
        Assert.Equal(5, result.Value.Items.Count);
    }

    [Fact]
    public async Task Get_IsTruncated_When_Audit_Page_Hits_The_Cap()
    {
        var audit = new FakeAuditHistoryReader(
            [Entry("company.updated")], totalCount: ReportLimits.ExportRowLimit);

        var result = await GetHandler(audit).HandleAsync(
            new GetGovernanceAdministrativeChangesReportRequest(CompanyId), CancellationToken.None);

        Assert.True(result.Value!.IsTruncated);
    }

    [Fact]
    public async Task Export_Row_Set_Equals_Unpaged_Get_Result()
    {
        var actor = Guid.NewGuid();
        var entries = new[]
        {
            Entry("company.updated", actorUserId: actor, occurredAt: new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero)),
            Entry("role.assigned", actorUserId: actor, occurredAt: new DateTimeOffset(2026, 8, 2, 0, 0, 0, TimeSpan.Zero)),
            Entry("employee.updated", actorUserId: actor), // filtered out
        };

        var getResult = await GetHandler(new FakeAuditHistoryReader(entries)).HandleAsync(
            new GetGovernanceAdministrativeChangesReportRequest(CompanyId, PageSize: 1), CancellationToken.None);

        var exporter = new FakeReportExporter();
        var exportHandler = new ExportGovernanceAdministrativeChangesReportHandler(
            new FakeAuditHistoryReader(entries), new FakeUserEmailDirectoryReader(), exporter,
            TestReportExportAuditor.Create());
        await exportHandler.HandleAsync(
            new ExportGovernanceAdministrativeChangesReportRequest(CompanyId), CancellationToken.None);

        Assert.Equal(2, getResult.Value!.TotalCount);
        Assert.Equal(2, exporter.LastData!.Rows.Count);
        Assert.Equal(GovernanceAuditReportSupport.ColumnHeaders, exporter.LastData.ColumnHeaders);
        Assert.Equal("role.assigned", exporter.LastData.Rows[0][1]); // newest first
    }
}
