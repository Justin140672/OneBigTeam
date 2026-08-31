using HR.Infrastructure.Abstractions;
using HR.Modules.Reporting.Features.ExportGovernanceComplianceStatusReport;
using HR.Modules.Reporting.Features.GetGovernanceComplianceStatusReport;

namespace HR.Modules.Reporting.Tests;

public class GovernanceComplianceStatusReportValidatorTests
{
    private readonly GetGovernanceComplianceStatusReportValidator _get = new();
    private readonly ExportGovernanceComplianceStatusReportValidator _export = new();

    private static GetGovernanceComplianceStatusReportRequest ValidGet() => new(Guid.NewGuid());

    private static ExportGovernanceComplianceStatusReportRequest ValidExport() => new(Guid.NewGuid());

    [Fact]
    public void Get_Valid_Passes() => Assert.True(_get.Validate(ValidGet()).IsValid);

    [Fact]
    public void Export_Valid_Passes() => Assert.True(_export.Validate(ValidExport()).IsValid);

    [Fact]
    public void Get_Empty_CompanyId_Fails() =>
        Assert.False(_get.Validate(ValidGet() with { CompanyId = Guid.Empty }).IsValid);

    [Fact]
    public void Export_Empty_CompanyId_Fails() =>
        Assert.False(_export.Validate(ValidExport() with { CompanyId = Guid.Empty }).IsValid);

    [Theory]
    [InlineData(0)]
    [InlineData(201)]
    public void Get_Bad_PageSize_Fails(int pageSize) =>
        Assert.False(_get.Validate(ValidGet() with { PageSize = pageSize }).IsValid);

    [Theory]
    [InlineData(1)]
    [InlineData(200)]
    public void Get_PageSize_Boundary_Passes(int pageSize) =>
        Assert.True(_get.Validate(ValidGet() with { PageSize = pageSize }).IsValid);

    [Fact]
    public void Get_Page_Below_One_Fails() =>
        Assert.False(_get.Validate(ValidGet() with { Page = 0 }).IsValid);

    [Theory]
    [InlineData("ExpiringVisa")]
    [InlineData("expiringvisa")]
    [InlineData("ProbationReview")]
    [InlineData(null)]
    public void Get_Valid_Category_Passes(string? category) =>
        Assert.True(_get.Validate(ValidGet() with { Category = category }).IsValid);

    [Theory]
    [InlineData("NotACategory")]
    [InlineData("")]
    public void Get_Invalid_Category_Fails(string category)
    {
        var r = _get.Validate(ValidGet() with { Category = category });
        Assert.Contains(r.Errors, e => e.PropertyName == nameof(GetGovernanceComplianceStatusReportRequest.Category));
    }

    [Theory]
    [InlineData("Overdue")]
    [InlineData("duesoon")]
    [InlineData("Informational")]
    [InlineData(null)]
    public void Get_Valid_Severity_Passes(string? severity) =>
        Assert.True(_get.Validate(ValidGet() with { Severity = severity }).IsValid);

    [Theory]
    [InlineData("Critical")]
    [InlineData("")]
    public void Get_Invalid_Severity_Fails(string severity)
    {
        var r = _get.Validate(ValidGet() with { Severity = severity });
        Assert.Contains(r.Errors, e => e.PropertyName == nameof(GetGovernanceComplianceStatusReportRequest.Severity));
    }

    [Fact]
    public void Get_DueDateEnd_Before_Start_Fails()
    {
        var r = _get.Validate(ValidGet() with
        {
            DueDateStart = new DateOnly(2026, 6, 1),
            DueDateEnd = new DateOnly(2026, 5, 1),
        });
        Assert.Contains(r.Errors, e => e.PropertyName == nameof(GetGovernanceComplianceStatusReportRequest.DueDateEnd));
    }

    [Fact]
    public void Get_DueDateEnd_Equal_Start_Passes() =>
        Assert.True(_get.Validate(ValidGet() with
        {
            DueDateStart = new DateOnly(2026, 6, 1),
            DueDateEnd = new DateOnly(2026, 6, 1),
        }).IsValid);

    [Fact]
    public void Export_Undefined_Format_Fails() =>
        Assert.False(_export.Validate(ValidExport() with { Format = (ReportExportFormat)999 }).IsValid);

    [Fact]
    public void Export_Invalid_Category_Fails() =>
        Assert.False(_export.Validate(ValidExport() with { Category = "nope" }).IsValid);

    [Fact]
    public void Export_Invalid_Severity_Fails() =>
        Assert.False(_export.Validate(ValidExport() with { Severity = "nope" }).IsValid);

    [Fact]
    public void Export_DueDateEnd_Before_Start_Fails() =>
        Assert.False(_export.Validate(ValidExport() with
        {
            DueDateStart = new DateOnly(2026, 6, 1),
            DueDateEnd = new DateOnly(2026, 5, 1),
        }).IsValid);
}
