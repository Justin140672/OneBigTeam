using HR.Modules.Companies.Features.ListCustomers;

namespace HR.Modules.Companies.Tests;

public class ListCustomersValidatorTests
{
    private readonly ListCustomersValidator _validator = new();

    [Fact]
    public void Validate_Succeeds_When_Search_Is_Null()
    {
        var result = _validator.Validate(new ListCustomersRequest(null));

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_Succeeds_When_Search_Is_Empty()
    {
        var result = _validator.Validate(new ListCustomersRequest(""));

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_Succeeds_When_Search_Is_Reasonable_Length()
    {
        var result = _validator.Validate(new ListCustomersRequest("Acme Corp"));

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_Fails_When_Search_Exceeds_Max_Length()
    {
        var tooLong = new string('a', 201);

        var result = _validator.Validate(new ListCustomersRequest(tooLong));

        Assert.False(result.IsValid);
    }

    [Fact]
    public void Validate_Succeeds_When_Search_Is_Exactly_Max_Length()
    {
        var exactlyMax = new string('a', 200);

        var result = _validator.Validate(new ListCustomersRequest(exactlyMax));

        Assert.True(result.IsValid);
    }
}
