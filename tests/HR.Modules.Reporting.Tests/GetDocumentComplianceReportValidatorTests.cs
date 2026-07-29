using HR.Modules.Reporting.Features.GetDocumentComplianceReport;

namespace HR.Modules.Reporting.Tests;

public class GetDocumentComplianceReportValidatorTests
{
    private readonly GetDocumentComplianceReportValidator _validator = new();

    [Fact]
    public void Should_Not_Have_Error_For_Valid_Request()
    {
        var result = _validator.Validate(new GetDocumentComplianceReportRequest(Guid.NewGuid(), null));

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Should_Not_Have_Error_When_PositionProfileId_Supplied()
    {
        var result = _validator.Validate(new GetDocumentComplianceReportRequest(Guid.NewGuid(), Guid.NewGuid()));

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Should_Have_Error_When_CompanyId_Is_Empty()
    {
        var result = _validator.Validate(new GetDocumentComplianceReportRequest(Guid.Empty, null));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(GetDocumentComplianceReportRequest.CompanyId));
    }
}
