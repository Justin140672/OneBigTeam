using HR.Infrastructure.Abstractions;
using AdminGet = HR.Modules.Reporting.Features.GetGovernanceAdministrativeChangesReport;
using AdminExport = HR.Modules.Reporting.Features.ExportGovernanceAdministrativeChangesReport;
using SecGet = HR.Modules.Reporting.Features.GetGovernanceSecurityEventsReport;
using SecExport = HR.Modules.Reporting.Features.ExportGovernanceSecurityEventsReport;

namespace HR.Modules.Reporting.Tests;

/// <summary>
/// ADM-08: the Administrative Changes and Security Events governance reports share the exact same
/// audit-filter validation rules as User Activity; these tests pin the boundary/negation/whitespace
/// cases for their Get and Export validators.
/// </summary>
public class GovernanceAuditReportValidatorTests
{
    private readonly AdminGet.GetGovernanceAdministrativeChangesReportValidator _adminGet = new();
    private readonly AdminExport.ExportGovernanceAdministrativeChangesReportValidator _adminExport = new();
    private readonly SecGet.GetGovernanceSecurityEventsReportValidator _secGet = new();
    private readonly SecExport.ExportGovernanceSecurityEventsReportValidator _secExport = new();

    // ── Administrative Changes ─────────────────────────────────────────────

    [Fact]
    public void AdminGet_Valid_Passes() =>
        Assert.True(_adminGet.Validate(new AdminGet.GetGovernanceAdministrativeChangesReportRequest(Guid.NewGuid())).IsValid);

    [Fact]
    public void AdminGet_Empty_CompanyId_Fails() =>
        Assert.False(_adminGet.Validate(new AdminGet.GetGovernanceAdministrativeChangesReportRequest(Guid.Empty)).IsValid);

    [Theory]
    [InlineData(0)]
    [InlineData(201)]
    public void AdminGet_Bad_PageSize_Fails(int pageSize) =>
        Assert.False(_adminGet.Validate(
            new AdminGet.GetGovernanceAdministrativeChangesReportRequest(Guid.NewGuid()) with { PageSize = pageSize }).IsValid);

    [Fact]
    public void AdminGet_Page_Below_One_Fails() =>
        Assert.False(_adminGet.Validate(
            new AdminGet.GetGovernanceAdministrativeChangesReportRequest(Guid.NewGuid()) with { Page = 0 }).IsValid);

    [Theory]
    [InlineData("bogus")]
    [InlineData(" ")]
    public void AdminGet_Bad_Status_Fails(string status) =>
        Assert.False(_adminGet.Validate(
            new AdminGet.GetGovernanceAdministrativeChangesReportRequest(Guid.NewGuid()) with { Status = status }).IsValid);

    [Fact]
    public void AdminGet_Reversed_Dates_Fails() =>
        Assert.False(_adminGet.Validate(
            new AdminGet.GetGovernanceAdministrativeChangesReportRequest(Guid.NewGuid())
            {
                FromDate = new DateOnly(2026, 6, 1),
                ToDate = new DateOnly(2026, 5, 1),
            }).IsValid);

    [Fact]
    public void AdminExport_Valid_Passes() =>
        Assert.True(_adminExport.Validate(new AdminExport.ExportGovernanceAdministrativeChangesReportRequest(Guid.NewGuid())).IsValid);

    [Fact]
    public void AdminExport_Undefined_Format_Fails() =>
        Assert.False(_adminExport.Validate(
            new AdminExport.ExportGovernanceAdministrativeChangesReportRequest(Guid.NewGuid()) with
            {
                Format = (ReportExportFormat)999,
            }).IsValid);

    [Fact]
    public void AdminExport_Reversed_Dates_Fails() =>
        Assert.False(_adminExport.Validate(
            new AdminExport.ExportGovernanceAdministrativeChangesReportRequest(Guid.NewGuid())
            {
                FromDate = new DateOnly(2026, 6, 1),
                ToDate = new DateOnly(2026, 5, 1),
            }).IsValid);

    // ── Security Events ───────────────────────────────────────────────────

    [Fact]
    public void SecGet_Valid_Passes() =>
        Assert.True(_secGet.Validate(new SecGet.GetGovernanceSecurityEventsReportRequest(Guid.NewGuid())).IsValid);

    [Fact]
    public void SecGet_Empty_CompanyId_Fails() =>
        Assert.False(_secGet.Validate(new SecGet.GetGovernanceSecurityEventsReportRequest(Guid.Empty)).IsValid);

    [Theory]
    [InlineData(0)]
    [InlineData(201)]
    public void SecGet_Bad_PageSize_Fails(int pageSize) =>
        Assert.False(_secGet.Validate(
            new SecGet.GetGovernanceSecurityEventsReportRequest(Guid.NewGuid()) with { PageSize = pageSize }).IsValid);

    [Theory]
    [InlineData(1)]
    [InlineData(200)]
    public void SecGet_PageSize_Boundary_Passes(int pageSize) =>
        Assert.True(_secGet.Validate(
            new SecGet.GetGovernanceSecurityEventsReportRequest(Guid.NewGuid()) with { PageSize = pageSize }).IsValid);

    [Theory]
    [InlineData("bogus")]
    [InlineData("")]
    [InlineData(" ")]
    public void SecGet_Bad_Status_Fails(string status) =>
        Assert.False(_secGet.Validate(
            new SecGet.GetGovernanceSecurityEventsReportRequest(Guid.NewGuid()) with { Status = status }).IsValid);

    [Theory]
    [InlineData("Success")]
    [InlineData("failed")]
    public void SecGet_Good_Status_Passes(string status) =>
        Assert.True(_secGet.Validate(
            new SecGet.GetGovernanceSecurityEventsReportRequest(Guid.NewGuid()) with { Status = status }).IsValid);

    [Fact]
    public void SecGet_Reversed_Dates_Fails() =>
        Assert.False(_secGet.Validate(
            new SecGet.GetGovernanceSecurityEventsReportRequest(Guid.NewGuid())
            {
                FromDate = new DateOnly(2026, 6, 1),
                ToDate = new DateOnly(2026, 5, 1),
            }).IsValid);

    [Fact]
    public void SecExport_Valid_Passes() =>
        Assert.True(_secExport.Validate(new SecExport.ExportGovernanceSecurityEventsReportRequest(Guid.NewGuid())).IsValid);

    [Fact]
    public void SecExport_Undefined_Format_Fails() =>
        Assert.False(_secExport.Validate(
            new SecExport.ExportGovernanceSecurityEventsReportRequest(Guid.NewGuid()) with
            {
                Format = (ReportExportFormat)999,
            }).IsValid);

    [Fact]
    public void SecExport_Bad_Status_Fails() =>
        Assert.False(_secExport.Validate(
            new SecExport.ExportGovernanceSecurityEventsReportRequest(Guid.NewGuid()) with { Status = "nope" }).IsValid);
}
