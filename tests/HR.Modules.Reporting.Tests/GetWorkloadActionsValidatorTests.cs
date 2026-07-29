using HR.Modules.Reporting.Features.GetWorkloadActions;

namespace HR.Modules.Reporting.Tests;

public class GetWorkloadActionsValidatorTests
{
    private readonly GetWorkloadActionsValidator _validator = new();

    [Fact]
    public void Validate_Fails_When_CompanyId_Is_Empty()
    {
        var result = _validator.Validate(new GetWorkloadActionsRequest(Guid.Empty));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(GetWorkloadActionsRequest.CompanyId));
    }

    [Fact]
    public void Validate_Succeeds_When_CompanyId_Is_Set_And_GroupBy_Is_Null()
    {
        var result = _validator.Validate(new GetWorkloadActionsRequest(Guid.NewGuid()));

        Assert.True(result.IsValid);
    }

    [Theory]
    [InlineData("ActionType")]
    [InlineData("AssignedUser")]
    [InlineData("Department")]
    [InlineData("DueDate")]
    public void Validate_Succeeds_For_Each_Allowed_GroupBy_Value(string groupBy)
    {
        var result = _validator.Validate(new GetWorkloadActionsRequest(Guid.NewGuid(), GroupBy: groupBy));

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_Fails_When_GroupBy_Is_Not_One_Of_The_Allowed_Values()
    {
        var result = _validator.Validate(new GetWorkloadActionsRequest(Guid.NewGuid(), GroupBy: "NotARealGroupByKey"));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(GetWorkloadActionsRequest.GroupBy));
    }
}
