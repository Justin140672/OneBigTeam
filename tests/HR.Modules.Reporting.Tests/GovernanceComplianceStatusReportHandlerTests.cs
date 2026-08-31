using HR.Infrastructure.Abstractions;
using HR.Modules.Reporting.Features.ExportGovernanceComplianceStatusReport;
using HR.Modules.Reporting.Features.GetComplianceCentre;
using HR.Modules.Reporting.Features.GetGovernanceComplianceStatusReport;
using HR.Modules.Reporting.Tests.Infrastructure;

namespace HR.Modules.Reporting.Tests;

public class GovernanceComplianceStatusReportHandlerTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 8, 30, 9, 0, 0, DateTimeKind.Utc);
    private static readonly DateOnly Today = DateOnly.FromDateTime(FixedUtcNow);
    private static readonly Guid CompanyId = Guid.NewGuid();

    private static ExpiringEmployeeDocumentItem Doc(
        Guid employeeId,
        ComplianceDocumentKind kind = ComplianceDocumentKind.Other,
        DateOnly? expiry = null) =>
        new(employeeId, "Doc", "Type", expiry ?? Today.AddDays(5), kind);

    private static EmployeeDirectoryReportItem DirectoryItem(Guid employeeId, string name = "Alice Smith", string dept = "Engineering") =>
        new(employeeId, "EMP-001", name, dept, "Engineer", "Manager", "Full-Time",
            new DateOnly(2026, 1, 1), "Active", "London", "alice@example.com");

    private static GetComplianceCentreHandler CentreHandler(
        IReadOnlyList<ExpiringEmployeeDocumentItem>? expiring = null,
        IReadOnlyList<EmployeeDirectoryReportItem>? directory = null) =>
        new(
            new FakeExpiringEmployeeDocumentReader(expiring),
            new FakeDocumentComplianceReportReader([]),
            new FakeOutstandingDocumentRequestComplianceReader(),
            new FakeProbationReviewComplianceReader(),
            new FakeEmployeeDirectoryReader(directory ?? []),
            new FakeClock(FixedUtcNow));

    private static GetGovernanceComplianceStatusReportHandler GetHandler(GetComplianceCentreHandler centre) =>
        new(centre);

    [Fact]
    public async Task Get_Maps_Rows_From_The_Compliance_Centre_Composition()
    {
        var employeeId = Guid.NewGuid();
        var handler = GetHandler(CentreHandler(
            expiring: [Doc(employeeId, ComplianceDocumentKind.Immigration, Today.AddDays(-1))],
            directory: [DirectoryItem(employeeId)]));

        var result = await handler.HandleAsync(
            new GetGovernanceComplianceStatusReportRequest(CompanyId), CancellationToken.None);

        var row = Assert.Single(result.Value!.Items);
        Assert.Equal(employeeId, row.EmployeeId);
        Assert.Equal("Alice Smith", row.EmployeeName);
        Assert.Equal("Engineering", row.Department);
        Assert.Equal("ExpiringVisa", row.Category);
        Assert.Equal("Overdue", row.Severity);
    }

    [Fact]
    public async Task Get_Pages_In_Memory_And_Reports_Full_TotalCount()
    {
        var expiring = Enumerable.Range(0, 8)
            .Select(_ => Doc(Guid.NewGuid(), expiry: Today.AddDays(3)))
            .ToList();
        var handler = GetHandler(CentreHandler(expiring: expiring));

        var result = await handler.HandleAsync(
            new GetGovernanceComplianceStatusReportRequest(CompanyId, Page: 2, PageSize: 3),
            CancellationToken.None);

        Assert.Equal(8, result.Value!.TotalCount);
        Assert.Equal(3, result.Value.Items.Count);
        Assert.Equal(2, result.Value.Page);
    }

    [Fact]
    public async Task Get_Passes_Category_Filter_Through_To_Compliance_Centre()
    {
        var visaEmp = Guid.NewGuid();
        var certEmp = Guid.NewGuid();
        var handler = GetHandler(CentreHandler(expiring:
        [
            Doc(visaEmp, ComplianceDocumentKind.Immigration),
            Doc(certEmp, ComplianceDocumentKind.Certification),
        ]));

        var result = await handler.HandleAsync(
            new GetGovernanceComplianceStatusReportRequest(CompanyId, Category: "ExpiringVisa"),
            CancellationToken.None);

        var row = Assert.Single(result.Value!.Items);
        Assert.Equal("ExpiringVisa", row.Category);
    }

    [Fact]
    public async Task Get_Passes_Severity_Filter_Through_To_Compliance_Centre()
    {
        var handler = GetHandler(CentreHandler(expiring:
        [
            Doc(Guid.NewGuid(), expiry: Today.AddDays(-1)), // Overdue
            Doc(Guid.NewGuid(), expiry: Today.AddDays(10)), // DueSoon
        ]));

        var result = await handler.HandleAsync(
            new GetGovernanceComplianceStatusReportRequest(CompanyId, Severity: "Overdue"),
            CancellationToken.None);

        var row = Assert.Single(result.Value!.Items);
        Assert.Equal("Overdue", row.Severity);
    }

    [Fact]
    public async Task Export_Uses_Expected_Headers_And_Row_Set_Matches_Unpaged_Get()
    {
        var expiring = Enumerable.Range(0, 5)
            .Select(_ => Doc(Guid.NewGuid(), expiry: Today.AddDays(3)))
            .ToList();

        var getResult = await GetHandler(CentreHandler(expiring: expiring)).HandleAsync(
            new GetGovernanceComplianceStatusReportRequest(CompanyId, PageSize: 2), CancellationToken.None);

        var exporter = new FakeReportExporter();
        await new ExportGovernanceComplianceStatusReportHandler(
                CentreHandler(expiring: expiring), exporter, TestReportExportAuditor.Create())
            .HandleAsync(new ExportGovernanceComplianceStatusReportRequest(CompanyId), CancellationToken.None);

        Assert.Equal(5, getResult.Value!.TotalCount);
        Assert.Equal(5, exporter.LastData!.Rows.Count);
        Assert.Equal(
            new[] { "Employee", "Department", "Category", "Detail", "Due Date", "Severity" },
            exporter.LastData.ColumnHeaders);
    }

    [Fact]
    public void Export_Headers_Contain_No_Sensitive_Tokens()
    {
        string[] sensitive = ["salary", "national insurance", " ni ", "nino", "bank", "sort code", "token", "password"];
        string[] headers = ["Employee", "Department", "Category", "Detail", "Due Date", "Severity"];

        foreach (var header in headers)
        {
            var lower = header.ToLowerInvariant();
            Assert.DoesNotContain(sensitive, t => lower.Contains(t));
        }
    }
}
