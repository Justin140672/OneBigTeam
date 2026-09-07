using HR.Modules.Marketing.Domain;
using HR.Modules.Marketing.Features.CreateMarketingRoadmapItem;
using HR.Modules.Marketing.Features.UpdateMarketingRoadmapItem;

namespace HR.Modules.Marketing.Tests;

public class MarketingRoadmapItemValidatorTests
{
    private readonly CreateMarketingRoadmapItemValidator _createValidator = new();
    private readonly UpdateMarketingRoadmapItemValidator _updateValidator = new();

    private static CreateMarketingRoadmapItemRequest ValidCreate() => new(
        Title: "AI position profiles",
        Description: "AI-assisted drafting.",
        IconName: "folder-open",
        DeliveryStatus: MarketingDeliveryStatus.ComingSoon,
        DisplayOrder: 0);

    private static UpdateMarketingRoadmapItemRequest ValidUpdate() => new(
        Id: Guid.NewGuid(),
        Title: "AI position profiles",
        Description: "AI-assisted drafting.",
        IconName: "folder-open",
        DeliveryStatus: MarketingDeliveryStatus.ComingSoon,
        DisplayOrder: 0);

    [Fact]
    public void Create_Passes_For_Valid_Request() => Assert.True(_createValidator.Validate(ValidCreate()).IsValid);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_Fails_When_Title_Is_Null_Empty_Or_Whitespace(string? title)
    {
        Assert.False(_createValidator.Validate(ValidCreate() with { Title = title! }).IsValid);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_Fails_When_Description_Is_Null_Empty_Or_Whitespace(string? description)
    {
        Assert.False(_createValidator.Validate(ValidCreate() with { Description = description! }).IsValid);
    }

    [Fact]
    public void Create_Title_Length_Boundary_200_Passes_201_Fails()
    {
        Assert.True(_createValidator.Validate(ValidCreate() with { Title = new string('t', 200) }).IsValid);
        Assert.False(_createValidator.Validate(ValidCreate() with { Title = new string('t', 201) }).IsValid);
    }

    [Fact]
    public void Create_Description_Length_Boundary_4000_Passes_4001_Fails()
    {
        Assert.True(_createValidator.Validate(ValidCreate() with { Description = new string('d', 4000) }).IsValid);
        Assert.False(_createValidator.Validate(ValidCreate() with { Description = new string('d', 4001) }).IsValid);
    }

    [Fact]
    public void Create_DisplayOrder_Boundary_Zero_Passes_Negative_Fails()
    {
        Assert.True(_createValidator.Validate(ValidCreate() with { DisplayOrder = 0 }).IsValid);
        Assert.False(_createValidator.Validate(ValidCreate() with { DisplayOrder = -1 }).IsValid);
    }

    [Fact]
    public void Update_Passes_For_Valid_Request() => Assert.True(_updateValidator.Validate(ValidUpdate()).IsValid);

    [Fact]
    public void Update_Fails_When_Id_Is_Empty()
    {
        Assert.False(_updateValidator.Validate(ValidUpdate() with { Id = Guid.Empty }).IsValid);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Update_Fails_When_Title_Is_Null_Empty_Or_Whitespace(string? title)
    {
        Assert.False(_updateValidator.Validate(ValidUpdate() with { Title = title! }).IsValid);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Update_Fails_When_Description_Is_Null_Empty_Or_Whitespace(string? description)
    {
        Assert.False(_updateValidator.Validate(ValidUpdate() with { Description = description! }).IsValid);
    }
}
