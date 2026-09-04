using HR.Modules.Employees.Features.GetEqualityDiversityReport;

namespace HR.Modules.Employees.Tests;

public class GetEqualityDiversityReportValidatorTests
{
    private static readonly GetEqualityDiversityReportValidator Validator = new();

    [Fact]
    public void Fails_When_CompanyId_Is_Empty()
        => Assert.False(Validator.Validate(new GetEqualityDiversityReportRequest(Guid.Empty)).IsValid);

    [Fact]
    public void Passes_When_CompanyId_Is_Non_Empty()
        => Assert.True(Validator.Validate(new GetEqualityDiversityReportRequest(Guid.NewGuid())).IsValid);
}
