using HR.Modules.Leave.Features.GetRecentLeaveRequests;

namespace HR.Modules.Leave.Tests;

public class GetRecentLeaveRequestsValidatorTests
{
    private readonly GetRecentLeaveRequestsValidator _validator = new();

    [Fact]
    public void Validate_Fails_When_CompanyId_Is_Empty()
    {
        var result = _validator.Validate(new GetRecentLeaveRequestsRequest(Guid.Empty, null));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(GetRecentLeaveRequestsRequest.CompanyId));
    }

    [Fact]
    public void Validate_Passes_When_Take_Is_Null()
    {
        var result = _validator.Validate(new GetRecentLeaveRequestsRequest(Guid.NewGuid(), null));

        Assert.True(result.IsValid);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(25)]
    [InlineData(50)]
    public void Validate_Passes_For_Take_Within_Range(int take)
    {
        var result = _validator.Validate(new GetRecentLeaveRequestsRequest(Guid.NewGuid(), take));

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_Fails_When_Take_Is_Zero()
    {
        var result = _validator.Validate(new GetRecentLeaveRequestsRequest(Guid.NewGuid(), 0));

        Assert.False(result.IsValid);
    }

    [Fact]
    public void Validate_Fails_When_Take_Exceeds_Fifty()
    {
        var result = _validator.Validate(new GetRecentLeaveRequestsRequest(Guid.NewGuid(), 51));

        Assert.False(result.IsValid);
    }

    [Fact]
    public void Validate_Fails_When_Take_Is_Negative()
    {
        var result = _validator.Validate(new GetRecentLeaveRequestsRequest(Guid.NewGuid(), -1));

        Assert.False(result.IsValid);
    }
}
