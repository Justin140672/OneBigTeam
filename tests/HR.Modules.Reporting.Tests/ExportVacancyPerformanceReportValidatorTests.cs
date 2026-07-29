using HR.Infrastructure.Abstractions;
using HR.Modules.Reporting.Features.ExportVacancyPerformanceReport;

namespace HR.Modules.Reporting.Tests;

public class ExportVacancyPerformanceReportValidatorTests
{
    private readonly ExportVacancyPerformanceReportValidator _validator = new();

    private static ExportVacancyPerformanceReportRequest ValidRequest() => new(Guid.NewGuid());

    [Fact]
    public void Should_Not_Have_Error_For_Valid_Request()
    {
        var result = _validator.Validate(ValidRequest());

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Should_Have_Error_When_CompanyId_Is_Empty()
    {
        var request = ValidRequest() with { CompanyId = Guid.Empty };

        var result = _validator.Validate(request);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(ExportVacancyPerformanceReportRequest.CompanyId));
    }

    [Fact]
    public void Should_Have_Error_When_Format_Is_Invalid()
    {
        var request = ValidRequest() with { Format = (ReportExportFormat)999 };

        var result = _validator.Validate(request);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(ExportVacancyPerformanceReportRequest.Format));
    }

    [Fact]
    public void Should_Have_Error_When_EndDate_Is_Before_StartDate()
    {
        var request = ValidRequest() with
        {
            StartDate = new DateOnly(2026, 6, 1),
            EndDate = new DateOnly(2026, 5, 1),
        };

        var result = _validator.Validate(request);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(ExportVacancyPerformanceReportRequest.EndDate));
    }
}
