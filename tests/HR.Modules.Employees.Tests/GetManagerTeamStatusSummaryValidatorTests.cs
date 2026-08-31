using HR.Modules.Employees.Features.GetManagerTeamStatusSummary;

namespace HR.Modules.Employees.Tests;

public class GetManagerTeamStatusSummaryValidatorTests
{
    private readonly GetManagerTeamStatusSummaryValidator _validator = new();

    [Fact]
    public void Validate_Fails_When_CompanyId_Is_Empty()
    {
        var result = _validator.Validate(new GetManagerTeamStatusSummaryRequest(Guid.Empty, Guid.NewGuid()));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(GetManagerTeamStatusSummaryRequest.CompanyId));
    }

    [Fact]
    public void Validate_Fails_When_ManagerId_Is_Empty()
    {
        var result = _validator.Validate(new GetManagerTeamStatusSummaryRequest(Guid.NewGuid(), Guid.Empty));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(GetManagerTeamStatusSummaryRequest.ManagerId));
    }

    [Fact]
    public void Validate_Fails_When_Both_Ids_Are_Empty()
    {
        var result = _validator.Validate(new GetManagerTeamStatusSummaryRequest(Guid.Empty, Guid.Empty));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(GetManagerTeamStatusSummaryRequest.CompanyId));
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(GetManagerTeamStatusSummaryRequest.ManagerId));
    }

    [Fact]
    public void Validate_Passes_For_Valid_Request()
    {
        var result = _validator.Validate(new GetManagerTeamStatusSummaryRequest(Guid.NewGuid(), Guid.NewGuid()));

        Assert.True(result.IsValid);
    }
}
