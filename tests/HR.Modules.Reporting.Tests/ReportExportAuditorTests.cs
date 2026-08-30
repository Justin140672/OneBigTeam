using HR.Infrastructure.Abstractions;
using HR.Modules.Reporting.ReportRegistry;
using HR.Modules.Reporting.Services;
using HR.Modules.Reporting.Tests.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;

namespace HR.Modules.Reporting.Tests;

public class ReportExportAuditorTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 8, 1, 12, 30, 0, DateTimeKind.Utc);

    private sealed record FakeExportRequest(Guid CompanyId, Guid? DepartmentId, DateOnly? DateRangeStart, ReportExportFormat Format);

    private static ReportExportAuditor BuildAuditor(
        out FakeAuditEventPublisher publisher, Guid? actorUserId = null)
        => BuildAuditor(out publisher, out _, actorUserId);

    private static ReportExportAuditor BuildAuditor(
        out FakeAuditEventPublisher publisher,
        out CapturingAdministrativeAlertWriter alertWriter,
        Guid? actorUserId = null)
    {
        publisher = new FakeAuditEventPublisher();
        alertWriter = new CapturingAdministrativeAlertWriter();
        return new ReportExportAuditor(
            publisher,
            alertWriter,
            new FakeClock(FixedUtcNow),
            new FakeCurrentUser(actorUserId ?? Guid.NewGuid()),
            NullLogger<ReportExportAuditor>.Instance);
    }

    [Fact]
    public async Task PublishFailureAsync_Raises_One_ReportGeneration_Administrative_Alert()
    {
        var auditor = BuildAuditor(out _, out var alertWriter);
        var companyId = Guid.NewGuid();
        var request = new FakeExportRequest(companyId, null, null, ReportExportFormat.Csv);

        await auditor.PublishFailureAsync(companyId, "employee-directory", "Csv", managerScopeApplied: false, request, "boom", CancellationToken.None);

        var command = Assert.Single(alertWriter.Commands);
        Assert.Equal(companyId, command.CompanyId);
        Assert.Equal(HR.Infrastructure.Abstractions.AdministrativeAlertCategory.ReportGeneration, command.Category);
        Assert.Equal("report-generation:employee-directory", command.DedupKey);
    }

    [Fact]
    public async Task PublishSuccessAsync_Raises_No_Administrative_Alert()
    {
        var auditor = BuildAuditor(out _, out var alertWriter);
        var companyId = Guid.NewGuid();
        var request = new FakeExportRequest(companyId, null, null, ReportExportFormat.Csv);

        await auditor.PublishSuccessAsync(companyId, "employee-directory", "Csv", rowCount: 1, managerScopeApplied: false, request, CancellationToken.None);

        Assert.Empty(alertWriter.Commands);
    }

    [Fact]
    public async Task PublishSuccessAsync_Publishes_Event_With_Success_True_And_Expected_Core_Fields()
    {
        var auditor = BuildAuditor(out var publisher);
        var companyId = Guid.NewGuid();
        var departmentId = Guid.NewGuid();
        var request = new FakeExportRequest(companyId, departmentId, new DateOnly(2026, 1, 1), ReportExportFormat.Csv);

        await auditor.PublishSuccessAsync(companyId, "employee-directory", "Csv", rowCount: 42, managerScopeApplied: false, request, CancellationToken.None);

        var published = Assert.Single(publisher.Published);
        var evt = Assert.IsType<ReportExportAuditEvent>(published);
        Assert.True(evt.Success);
        Assert.Equal(42, evt.RowCount);
        Assert.Equal(companyId, evt.CompanyId);
        Assert.Equal("employee-directory", evt.ReportId);
        Assert.Equal("Csv", evt.Format);
        Assert.False(evt.ManagerScopeApplied);
        Assert.Null(evt.FailureReason);
        Assert.Equal(ReportSensitivity.Sensitive.ToString(), evt.Sensitivity);
    }

    [Fact]
    public async Task PublishSuccessAsync_Resolves_Sensitivity_From_ReportCatalog_For_A_Standard_Report()
    {
        var auditor = BuildAuditor(out var publisher);
        var companyId = Guid.NewGuid();
        var request = new FakeExportRequest(companyId, null, null, ReportExportFormat.Pdf);

        await auditor.PublishSuccessAsync(companyId, "recruitment-pipeline-summary", "Pdf", rowCount: 3, managerScopeApplied: false, request, CancellationToken.None);

        var evt = Assert.IsType<ReportExportAuditEvent>(Assert.Single(publisher.Published));
        Assert.Equal(ReportSensitivity.Standard.ToString(), evt.Sensitivity);
    }

    [Fact]
    public async Task PublishSuccessAsync_Filters_Excludes_CompanyId_And_Format_But_Includes_Other_Request_Properties()
    {
        var auditor = BuildAuditor(out var publisher);
        var companyId = Guid.NewGuid();
        var departmentId = Guid.NewGuid();
        var startDate = new DateOnly(2026, 3, 15);
        var request = new FakeExportRequest(companyId, departmentId, startDate, ReportExportFormat.Csv);

        await auditor.PublishSuccessAsync(companyId, "employee-directory", "Csv", rowCount: 1, managerScopeApplied: false, request, CancellationToken.None);

        var evt = Assert.IsType<ReportExportAuditEvent>(Assert.Single(publisher.Published));

        Assert.DoesNotContain("CompanyId", evt.Filters.Keys);
        Assert.DoesNotContain("Format", evt.Filters.Keys);
        Assert.Equal(new[] { "DepartmentId", "DateRangeStart" }.OrderBy(x => x), evt.Filters.Keys.OrderBy(x => x));
        Assert.Equal(departmentId.ToString(), evt.Filters["DepartmentId"]);
        Assert.Equal(startDate.ToString(), evt.Filters["DateRangeStart"]);
    }

    [Fact]
    public async Task PublishSuccessAsync_Filters_Never_Contain_Row_Level_Content_Only_Structured_Request_Properties()
    {
        // The auditor never has access to the generated report's rows in the first place -- it only
        // ever sees the export request. Asserting the filter keys match exactly the request's own
        // property names (minus CompanyId/Format) pins that no row/PII data can leak in here.
        var auditor = BuildAuditor(out var publisher);
        var companyId = Guid.NewGuid();
        var request = new FakeExportRequest(companyId, null, null, ReportExportFormat.Csv);

        await auditor.PublishSuccessAsync(companyId, "employee-directory", "Csv", rowCount: 0, managerScopeApplied: false, request, CancellationToken.None);

        var evt = Assert.IsType<ReportExportAuditEvent>(Assert.Single(publisher.Published));

        var expectedKeys = typeof(FakeExportRequest).GetProperties()
            .Select(p => p.Name)
            .Where(name => name is not "CompanyId" and not "Format")
            .OrderBy(x => x);

        Assert.Equal(expectedKeys, evt.Filters.Keys.OrderBy(x => x));
    }

    [Fact]
    public async Task PublishFailureAsync_Publishes_Event_With_Success_False_RowCount_Null_And_FailureReason_Captured()
    {
        var auditor = BuildAuditor(out var publisher);
        var companyId = Guid.NewGuid();
        var request = new FakeExportRequest(companyId, null, null, ReportExportFormat.Csv);

        await auditor.PublishFailureAsync(companyId, "employee-directory", "Csv", managerScopeApplied: false, request, "boom", CancellationToken.None);

        var evt = Assert.IsType<ReportExportAuditEvent>(Assert.Single(publisher.Published));
        Assert.False(evt.Success);
        Assert.Null(evt.RowCount);
        Assert.Equal("boom", evt.FailureReason);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ManagerScopeApplied_Is_Carried_Through_As_Passed(bool managerScopeApplied)
    {
        var auditor = BuildAuditor(out var publisher);
        var companyId = Guid.NewGuid();
        var request = new FakeExportRequest(companyId, null, null, ReportExportFormat.Csv);

        await auditor.PublishSuccessAsync(companyId, "probation-report", "Csv", rowCount: 1, managerScopeApplied, request, CancellationToken.None);

        var evt = Assert.IsType<ReportExportAuditEvent>(Assert.Single(publisher.Published));
        Assert.Equal(managerScopeApplied, evt.ManagerScopeApplied);
    }

    [Fact]
    public async Task Unknown_Report_Id_Falls_Back_To_Sensitive_In_Metadata()
    {
        var auditor = BuildAuditor(out var publisher);
        var companyId = Guid.NewGuid();
        var request = new FakeExportRequest(companyId, null, null, ReportExportFormat.Csv);

        await auditor.PublishSuccessAsync(companyId, "not-a-real-report-id", "Csv", rowCount: 1, managerScopeApplied: false, request, CancellationToken.None);

        var evt = Assert.IsType<ReportExportAuditEvent>(Assert.Single(publisher.Published));
        Assert.Equal(ReportSensitivity.Sensitive.ToString(), evt.Sensitivity);

        // Also verify the fail-closed default surfaces through the anonymous Metadata payload
        // (what actually gets persisted to the audit store), not just the strongly-typed property.
        var metadata = ((HR.SharedKernel.IAuditEvent)evt).Metadata;
        var sensitivityProperty = metadata!.GetType().GetProperty("Sensitivity");
        Assert.NotNull(sensitivityProperty);
        Assert.Equal(ReportSensitivity.Sensitive.ToString(), sensitivityProperty!.GetValue(metadata));
    }

    [Fact]
    public async Task PublishFailureAsync_Unknown_Report_Id_Also_Falls_Back_To_Sensitive()
    {
        var auditor = BuildAuditor(out var publisher);
        var companyId = Guid.NewGuid();
        var request = new FakeExportRequest(companyId, null, null, ReportExportFormat.Csv);

        await auditor.PublishFailureAsync(companyId, "not-a-real-report-id", "Csv", managerScopeApplied: false, request, "err", CancellationToken.None);

        var evt = Assert.IsType<ReportExportAuditEvent>(Assert.Single(publisher.Published));
        Assert.Equal(ReportSensitivity.Sensitive.ToString(), evt.Sensitivity);
        Assert.False(evt.Success);
    }
}
