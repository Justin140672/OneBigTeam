using HR.Modules.Reporting.Features.GetHrDashboardSummary;

namespace HR.Modules.Reporting.Tests;

public class GetHrDashboardSummaryValidatorTests
{
    private readonly GetHrDashboardSummaryValidator _validator = new();

    [Fact]
    public void Validate_Fails_When_CompanyId_Is_Empty()
    {
        var result = _validator.Validate(new GetHrDashboardSummaryRequest(Guid.Empty));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(GetHrDashboardSummaryRequest.CompanyId));
    }

    [Fact]
    public void Validate_Succeeds_When_CompanyId_Is_Set()
    {
        var result = _validator.Validate(new GetHrDashboardSummaryRequest(Guid.NewGuid()));

        Assert.True(result.IsValid);
    }
}
