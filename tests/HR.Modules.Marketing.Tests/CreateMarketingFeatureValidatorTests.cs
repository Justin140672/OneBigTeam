using HR.Modules.Marketing.Domain;
using HR.Modules.Marketing.Features.CreateMarketingFeature;

namespace HR.Modules.Marketing.Tests;

public class CreateMarketingFeatureValidatorTests
{
    private readonly CreateMarketingFeatureValidator _validator = new();

    private static CreateMarketingFeatureRequest Valid() => new(
        Slug: "employee-management",
        IconName: "users",
        Title: "Employee Management",
        Summary: "Summary text",
        Intro: "Intro text",
        DetailedContent: null,
        Benefits: ["a"],
        YouTubeId: "abc123",
        DisplayOrder: 0,
        DeliveryStatus: MarketingDeliveryStatus.Available);

    private bool IsValid(CreateMarketingFeatureRequest r) => _validator.Validate(r).IsValid;

    [Fact]
    public void Passes_For_Valid_Request() => Assert.True(IsValid(Valid()));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Fails_When_Slug_Is_Null_Empty_Or_Whitespace(string? slug)
    {
        Assert.False(IsValid(Valid() with { Slug = slug! }));
    }

    [Theory]
    [InlineData("Bad Slug")]
    [InlineData("bad_slug")]
    [InlineData("-leading")]
    [InlineData("trailing-")]
    [InlineData("double--hyphen")]
    public void Fails_For_Slug_Not_Matching_Pattern(string slug)
    {
        Assert.False(IsValid(Valid() with { Slug = slug }));
    }

    [Theory]
    [InlineData("employee-management")]
    [InlineData("reporting")]
    [InlineData("a1-b2-c3")]
    public void Passes_For_Slug_Matching_Pattern(string slug)
    {
        Assert.True(IsValid(Valid() with { Slug = slug }));
    }

    [Fact]
    public void Normalizes_Slug_Before_Pattern_Check_So_Uppercase_Input_Is_Accepted()
    {
        // The Must() rule lower-cases/trims before testing SlugPattern, mirroring MarketingFeature.Create.
        Assert.True(IsValid(Valid() with { Slug = "EMPLOYEE-MANAGEMENT" }));
        Assert.True(IsValid(Valid() with { Slug = "  employee-management  " }));
    }

    [Fact]
    public void Slug_Length_Boundary_100_Passes_101_Fails()
    {
        Assert.True(IsValid(Valid() with { Slug = new string('a', 100) }));
        Assert.False(IsValid(Valid() with { Slug = new string('a', 101) }));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Fails_When_Title_Is_Null_Empty_Or_Whitespace(string? title)
    {
        Assert.False(IsValid(Valid() with { Title = title! }));
    }

    [Fact]
    public void Title_Length_Boundary_200_Passes_201_Fails()
    {
        Assert.True(IsValid(Valid() with { Title = new string('t', 200) }));
        Assert.False(IsValid(Valid() with { Title = new string('t', 201) }));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Fails_When_Summary_Is_Null_Empty_Or_Whitespace(string? summary)
    {
        Assert.False(IsValid(Valid() with { Summary = summary! }));
    }

    [Fact]
    public void Summary_Length_Boundary_500_Passes_501_Fails()
    {
        Assert.True(IsValid(Valid() with { Summary = new string('s', 500) }));
        Assert.False(IsValid(Valid() with { Summary = new string('s', 501) }));
    }

    [Fact]
    public void IconName_Length_Boundary_100_Passes_101_Fails()
    {
        Assert.True(IsValid(Valid() with { IconName = new string('i', 100) }));
        Assert.False(IsValid(Valid() with { IconName = new string('i', 101) }));
    }

    [Fact]
    public void YouTubeId_Length_Boundary_32_Passes_33_Fails()
    {
        Assert.True(IsValid(Valid() with { YouTubeId = new string('y', 32) }));
        Assert.False(IsValid(Valid() with { YouTubeId = new string('y', 33) }));
    }

    [Fact]
    public void DisplayOrder_Boundary_Zero_Passes_Negative_Fails()
    {
        Assert.True(IsValid(Valid() with { DisplayOrder = 0 }));
        Assert.False(IsValid(Valid() with { DisplayOrder = -1 }));
    }
}
