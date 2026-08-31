using HR.Infrastructure.Abstractions;
using HR.Modules.Reporting.Features.ExportGovernanceUserActivityReport;
using HR.Modules.Reporting.Features.GetGovernanceUserActivityReport;

namespace HR.Modules.Reporting.Tests;

public class GovernanceUserActivityReportValidatorTests
{
    private readonly GetGovernanceUserActivityReportValidator _get = new();
    private readonly ExportGovernanceUserActivityReportValidator _export = new();

    private static GetGovernanceUserActivityReportRequest ValidGet() => new(Guid.NewGuid());

    private static ExportGovernanceUserActivityReportRequest ValidExport() => new(Guid.NewGuid());

    [Fact]
    public void Get_Valid_Request_Passes() => Assert.True(_get.Validate(ValidGet()).IsValid);

    [Fact]
    public void Export_Valid_Request_Passes() => Assert.True(_export.Validate(ValidExport()).IsValid);

    [Fact]
    public void Get_Empty_CompanyId_Fails()
    {
        var r = _get.Validate(ValidGet() with { CompanyId = Guid.Empty });
        Assert.Contains(r.Errors, e => e.PropertyName == nameof(GetGovernanceUserActivityReportRequest.CompanyId));
    }

    [Fact]
    public void Export_Empty_CompanyId_Fails()
    {
        var r = _export.Validate(ValidExport() with { CompanyId = Guid.Empty });
        Assert.Contains(r.Errors, e => e.PropertyName == nameof(ExportGovernanceUserActivityReportRequest.CompanyId));
    }

    [Fact]
    public void Get_Page_Below_One_Fails() =>
        Assert.False(_get.Validate(ValidGet() with { Page = 0 }).IsValid);

    [Theory]
    [InlineData(0)]
    [InlineData(201)]
    public void Get_PageSize_Out_Of_Range_Fails(int pageSize) =>
        Assert.False(_get.Validate(ValidGet() with { PageSize = pageSize }).IsValid);

    [Theory]
    [InlineData(1)]
    [InlineData(200)]
    public void Get_PageSize_At_Boundary_Passes(int pageSize) =>
        Assert.True(_get.Validate(ValidGet() with { PageSize = pageSize }).IsValid);

    [Theory]
    [InlineData("Success")]
    [InlineData("Failed")]
    [InlineData("failed")]
    [InlineData(null)]
    public void Get_Valid_Status_Passes(string? status) =>
        Assert.True(_get.Validate(ValidGet() with { Status = status }).IsValid);

    [Theory]
    [InlineData("Pending")]
    [InlineData("")]
    [InlineData(" ")]
    public void Get_Invalid_Status_Fails(string status) =>
        Assert.False(_get.Validate(ValidGet() with { Status = status }).IsValid);

    [Theory]
    [InlineData("Pending")]
    [InlineData(" ")]
    public void Export_Invalid_Status_Fails(string status) =>
        Assert.False(_export.Validate(ValidExport() with { Status = status }).IsValid);

    [Fact]
    public void Get_ToDate_Before_FromDate_Fails()
    {
        var r = _get.Validate(ValidGet() with
        {
            FromDate = new DateOnly(2026, 6, 1),
            ToDate = new DateOnly(2026, 5, 1),
        });
        Assert.Contains(r.Errors, e => e.PropertyName == nameof(GetGovernanceUserActivityReportRequest.ToDate));
    }

    [Fact]
    public void Get_ToDate_Equal_FromDate_Passes() =>
        Assert.True(_get.Validate(ValidGet() with
        {
            FromDate = new DateOnly(2026, 6, 1),
            ToDate = new DateOnly(2026, 6, 1),
        }).IsValid);

    [Fact]
    public void Get_Only_FromDate_Set_Passes() =>
        Assert.True(_get.Validate(ValidGet() with { FromDate = new DateOnly(2026, 6, 1) }).IsValid);

    [Fact]
    public void Export_ToDate_Before_FromDate_Fails() =>
        Assert.False(_export.Validate(ValidExport() with
        {
            FromDate = new DateOnly(2026, 6, 1),
            ToDate = new DateOnly(2026, 5, 1),
        }).IsValid);

    [Fact]
    public void Export_Undefined_Format_Fails()
    {
        var r = _export.Validate(ValidExport() with { Format = (ReportExportFormat)999 });
        Assert.Contains(r.Errors, e => e.PropertyName == nameof(ExportGovernanceUserActivityReportRequest.Format));
    }

    [Theory]
    [InlineData(ReportExportFormat.Csv)]
    [InlineData(ReportExportFormat.Excel)]
    [InlineData(ReportExportFormat.Pdf)]
    public void Export_Defined_Formats_Pass(ReportExportFormat format) =>
        Assert.True(_export.Validate(ValidExport() with { Format = format }).IsValid);
}
