using HR.Modules.Notifications.Features.SendProductUpdate;

namespace HR.Modules.Notifications.Tests.Features;

public class SendProductUpdateValidatorTests
{
    private static readonly SendProductUpdateValidator Validator = new();

    private static SendProductUpdateRequest ValidRequest(string? url = "/reports/recruitment-pipeline") =>
        new("New feature shipped", "We just released a new feature.", url);

    [Fact]
    public void Validate_ValidRequest_Passes()
    {
        Assert.True(Validator.Validate(ValidRequest()).IsValid);
    }


    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_MissingTitle_Fails(string? title)
    {
        var result = Validator.Validate(ValidRequest() with { Title = title! });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(SendProductUpdateRequest.Title));
    }

    [Fact]
    public void Validate_TitleAtMaximumLength_Passes()
    {
        var result = Validator.Validate(ValidRequest() with { Title = new string('a', 200) });
        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_TitleOverMaximumLength_Fails()
    {
        var result = Validator.Validate(ValidRequest() with { Title = new string('a', 201) });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(SendProductUpdateRequest.Title));
    }


    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_MissingMessage_Fails(string? message)
    {
        var result = Validator.Validate(ValidRequest() with { Message = message! });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(SendProductUpdateRequest.Message));
    }

    [Fact]
    public void Validate_MessageAtMaximumLength_Passes()
    {
        var result = Validator.Validate(ValidRequest() with { Message = new string('a', 4000) });
        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_MessageOverMaximumLength_Fails()
    {
        var result = Validator.Validate(ValidRequest() with { Message = new string('a', 4001) });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(SendProductUpdateRequest.Message));
    }


    [Fact]
    public void Validate_NullUrl_Passes()
    {
        var result = Validator.Validate(ValidRequest(url: null));
        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_EmptyUrl_Passes()
    {
        var result = Validator.Validate(ValidRequest(url: ""));
        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_UrlNotStartingWithSlash_Fails()
    {
        var result = Validator.Validate(ValidRequest(url: "reports/recruitment-pipeline"));
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(SendProductUpdateRequest.Url));
    }

    [Fact]
    public void Validate_UrlStartingWithDoubleSlash_Fails()
    {
        var result = Validator.Validate(ValidRequest(url: "//evil.example.com/phish"));
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(SendProductUpdateRequest.Url));
    }

    [Fact]
    public void Validate_ValidRelativeUrl_Passes()
    {
        var result = Validator.Validate(ValidRequest(url: "/reports/recruitment-pipeline"));
        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_UrlAtMaximumLength_Passes()
    {
        var result = Validator.Validate(ValidRequest(url: "/" + new string('a', 1999)));
        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_UrlOverMaximumLength_Fails()
    {
        var result = Validator.Validate(ValidRequest(url: "/" + new string('a', 2000)));
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(SendProductUpdateRequest.Url));
    }
}
