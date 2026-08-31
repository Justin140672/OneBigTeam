using HR.Infrastructure.Abstractions;
using HR.Modules.Reporting.Features.ExportGovernanceSecurityEventsReport;
using HR.Modules.Reporting.Features.GetGovernanceSecurityEventsReport;
using HR.Modules.Reporting.GovernanceReporting;
using HR.Modules.Reporting.ReportRegistry;
using HR.Modules.Reporting.Tests.Infrastructure;
using static HR.Modules.Reporting.Tests.Infrastructure.GovernanceAuditTestData;

namespace HR.Modules.Reporting.Tests;

public class GovernanceSecurityEventsReportHandlerTests
{
    private static readonly Guid CompanyId = Guid.NewGuid();

    private static GetGovernanceSecurityEventsReportHandler GetHandler(FakeAuditHistoryReader audit) =>
        new(audit, new FakeUserEmailDirectoryReader());

    [Fact]
    public async Task Get_Keeps_Only_Auth_And_AccountStatus_Events()
    {
        var actor = Guid.NewGuid();
        var audit = new FakeAuditHistoryReader(
        [
            Entry("login.failed", actorUserId: actor),
            Entry("session.revoked", actorUserId: actor),
            Entry("role.assigned", actorUserId: actor),
            Entry("company.settings-updated", actorUserId: actor), // out of scope here
            Entry("employee.created", actorUserId: actor),         // out of scope
        ]);

        var result = await GetHandler(audit).HandleAsync(
            new GetGovernanceSecurityEventsReportRequest(CompanyId), CancellationToken.None);

        Assert.Equal(3, result.Value!.TotalCount);
    }

    [Fact]
    public async Task Get_Status_Filter_Isolates_Failed_Events()
    {
        var actor = Guid.NewGuid();
        var audit = new FakeAuditHistoryReader(
        [
            Entry("login.failed", actorUserId: actor),
            Entry("login.succeeded", actorUserId: actor),
        ]);

        var result = await GetHandler(audit).HandleAsync(
            new GetGovernanceSecurityEventsReportRequest(CompanyId, Status: "Failed"), CancellationToken.None);

        var row = Assert.Single(result.Value!.Items);
        Assert.Equal("login.failed", row.EventType);
        Assert.Equal("Failed", row.Status);
    }

    [Fact]
    public async Task Get_Actor_Filter_Narrows_Correctly()
    {
        var wanted = Guid.NewGuid();
        var audit = new FakeAuditHistoryReader(
        [
            Entry("login.succeeded", actorUserId: wanted),
            Entry("login.succeeded", actorUserId: Guid.NewGuid()),
        ]);

        var result = await GetHandler(audit).HandleAsync(
            new GetGovernanceSecurityEventsReportRequest(CompanyId, ActorUserId: wanted), CancellationToken.None);

        Assert.Equal(1, result.Value!.TotalCount);
    }

    [Fact]
    public async Task Get_IsTruncated_When_Audit_Page_Hits_The_Cap()
    {
        var audit = new FakeAuditHistoryReader(
            [Entry("login.succeeded")], totalCount: ReportLimits.ExportRowLimit + 5);

        var result = await GetHandler(audit).HandleAsync(
            new GetGovernanceSecurityEventsReportRequest(CompanyId), CancellationToken.None);

        Assert.True(result.Value!.IsTruncated);
    }

    [Fact]
    public async Task Export_Row_Set_Equals_Unpaged_Get_Result()
    {
        var actor = Guid.NewGuid();
        var entries = new[]
        {
            Entry("login.failed", actorUserId: actor, occurredAt: new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero)),
            Entry("auth.mfa-passed", actorUserId: actor, occurredAt: new DateTimeOffset(2026, 8, 2, 0, 0, 0, TimeSpan.Zero)),
            Entry("employee.updated", actorUserId: actor),
        };

        var getResult = await GetHandler(new FakeAuditHistoryReader(entries)).HandleAsync(
            new GetGovernanceSecurityEventsReportRequest(CompanyId, PageSize: 1), CancellationToken.None);

        var exporter = new FakeReportExporter();
        await new ExportGovernanceSecurityEventsReportHandler(
                new FakeAuditHistoryReader(entries), new FakeUserEmailDirectoryReader(), exporter,
                TestReportExportAuditor.Create())
            .HandleAsync(new ExportGovernanceSecurityEventsReportRequest(CompanyId), CancellationToken.None);

        Assert.Equal(2, getResult.Value!.TotalCount);
        Assert.Equal(2, exporter.LastData!.Rows.Count);
        Assert.Equal(GovernanceAuditReportSupport.ColumnHeaders, exporter.LastData.ColumnHeaders);
    }
}
