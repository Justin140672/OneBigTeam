using HR.Modules.Reporting.Features.GetAssetAssignmentReport;

namespace HR.Modules.Reporting.Tests;

public class GetAssetAssignmentReportValidatorTests
{
    private readonly GetAssetAssignmentReportValidator _validator = new();

    [Fact]
    public void Should_Not_Have_Error_For_Valid_Request()
    {
        var result = _validator.Validate(new GetAssetAssignmentReportRequest(Guid.NewGuid()));

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Should_Have_Error_When_CompanyId_Is_Empty()
    {
        var result = _validator.Validate(new GetAssetAssignmentReportRequest(Guid.Empty));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(GetAssetAssignmentReportRequest.CompanyId));
    }
}
