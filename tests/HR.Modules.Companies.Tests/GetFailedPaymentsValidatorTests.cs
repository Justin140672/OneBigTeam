using HR.Modules.Companies.Features.GetFailedPayments;

namespace HR.Modules.Companies.Tests;

public class GetFailedPaymentsValidatorTests
{
    [Fact]
    public void Validate_Fails_When_Search_Exceeds_MaxLength()
    {
        var result = new GetFailedPaymentsValidator()
            .Validate(new GetFailedPaymentsRequest(new string('a', 201), null));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(GetFailedPaymentsRequest.Search));
    }

    [Fact]
    public void Validate_Passes_When_Search_Is_Within_MaxLength()
    {
        var result = new GetFailedPaymentsValidator()
            .Validate(new GetFailedPaymentsRequest(new string('a', 200), null));

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_Passes_When_Search_Is_Null()
    {
        var result = new GetFailedPaymentsValidator()
            .Validate(new GetFailedPaymentsRequest(null, null));

        Assert.True(result.IsValid);
    }

    [Theory]
    [InlineData("open")]
    [InlineData("uncollectible")]
    public void Validate_Passes_When_StatusFilter_Is_Allowed_Value(string statusFilter)
    {
        var result = new GetFailedPaymentsValidator()
            .Validate(new GetFailedPaymentsRequest(null, statusFilter));

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_Fails_When_StatusFilter_Is_Not_Allowed_Value()
    {
        var result = new GetFailedPaymentsValidator()
            .Validate(new GetFailedPaymentsRequest(null, "paid"));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(GetFailedPaymentsRequest.StatusFilter));
    }

    [Fact]
    public void Validate_Passes_When_StatusFilter_Is_Null()
    {
        var result = new GetFailedPaymentsValidator()
            .Validate(new GetFailedPaymentsRequest(null, null));

        Assert.True(result.IsValid);
    }
}
