using HR.Modules.Reporting.Features.GetManagerDashboardSummary;

namespace HR.Modules.Reporting.Tests;

public class GetManagerDashboardSummaryValidatorTests
{
    private readonly GetManagerDashboardSummaryValidator _validator = new();

    [Fact]
    public void Validate_Fails_When_CompanyId_Is_Empty()
    {
        var result = _validator.Validate(new GetManagerDashboardSummaryRequest(Guid.Empty));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(GetManagerDashboardSummaryRequest.CompanyId));
    }

    [Fact]
    public void Validate_Succeeds_When_CompanyId_Is_Set()
    {
        var result = _validator.Validate(new GetManagerDashboardSummaryRequest(Guid.NewGuid()));

        Assert.True(result.IsValid);
    }
}
