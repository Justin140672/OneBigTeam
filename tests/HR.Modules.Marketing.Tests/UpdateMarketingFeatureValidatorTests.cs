using HR.Modules.Marketing.Domain;
using HR.Modules.Marketing.Features.UpdateMarketingFeature;

namespace HR.Modules.Marketing.Tests;

public class UpdateMarketingFeatureValidatorTests
{
    private readonly UpdateMarketingFeatureValidator _validator = new();

    private static UpdateMarketingFeatureRequest Valid() => new(
        Id: Guid.NewGuid(),
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

    private bool IsValid(UpdateMarketingFeatureRequest r) => _validator.Validate(r).IsValid;

    [Fact]
    public void Passes_For_Valid_Request() => Assert.True(IsValid(Valid()));

    [Fact]
    public void Fails_When_Id_Is_Empty() => Assert.False(IsValid(Valid() with { Id = Guid.Empty }));

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
    public void Fails_For_Slug_Not_Matching_Pattern(string slug)
    {
        Assert.False(IsValid(Valid() with { Slug = slug }));
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
    public void DisplayOrder_Boundary_Zero_Passes_Negative_Fails()
    {
        Assert.True(IsValid(Valid() with { DisplayOrder = 0 }));
        Assert.False(IsValid(Valid() with { DisplayOrder = -1 }));
    }
}
