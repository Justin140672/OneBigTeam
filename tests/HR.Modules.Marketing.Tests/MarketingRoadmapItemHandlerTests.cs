using HR.Modules.Marketing.Domain;
using HR.Modules.Marketing.Features.CreateMarketingRoadmapItem;
using HR.Modules.Marketing.Features.GetMarketingContent;
using HR.Modules.Marketing.Features.ReorderMarketingRoadmapItems;
using HR.Modules.Marketing.Features.SetMarketingRoadmapItemPublication;
using HR.Modules.Marketing.Features.UpdateMarketingRoadmapItem;
using HR.Modules.Marketing.Persistence;
using HR.Modules.Marketing.Tests.Infrastructure;

using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Marketing.Tests;

public class MarketingRoadmapItemHandlerTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 6, 30, 10, 0, 0, DateTimeKind.Utc);

    private readonly MarketingDbContext _db = MarketingTestContext.Build();
    private readonly FakeAuditEventPublisher _audit = new();
    private readonly Guid _actor = Guid.NewGuid();

    private CreateMarketingRoadmapItemHandler CreateHandler() =>
        new(_db, new FakeCurrentUser(_actor), new FakeClock(FixedUtcNow), _audit);

    private UpdateMarketingRoadmapItemHandler UpdateHandler() =>
        new(_db, new FakeCurrentUser(_actor), new FakeClock(FixedUtcNow), _audit);

    private SetMarketingRoadmapItemPublicationHandler PublicationHandler() =>
        new(_db, new FakeCurrentUser(_actor), new FakeClock(FixedUtcNow), _audit);

    private ReorderMarketingRoadmapItemsHandler ReorderHandler() =>
        new(_db, new FakeCurrentUser(_actor), new FakeClock(FixedUtcNow), _audit);

    private static CreateMarketingRoadmapItemRequest ValidCreate(string title = "AI position profiles") => new(
        Title: title,
        Description: "AI-assisted drafting.",
        IconName: "folder-open",
        DeliveryStatus: MarketingDeliveryStatus.ComingSoon,
        DisplayOrder: 0);

    private async Task<Guid> SeedAsync(string title, int order = 0)
    {
        var result = await CreateHandler().HandleAsync(ValidCreate(title) with { DisplayOrder = order }, CancellationToken.None);
        Assert.True(result.IsSuccess);
        return result.Value!.Id;
    }

    [Fact]
    public async Task Create_Persists_Fields_Starts_Unpublished_And_Publishes_Audit()
    {
        var result = await CreateHandler().HandleAsync(ValidCreate(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value!.IsPublished);

        var saved = await _db.MarketingRoadmapItems.SingleAsync();
        Assert.Equal("AI position profiles", saved.Title);
        Assert.Equal("AI-assisted drafting.", saved.Description);
        Assert.Equal(MarketingDeliveryStatus.ComingSoon, saved.DeliveryStatus);
        Assert.Equal(_actor, saved.CreatedByUserId);
        Assert.Contains(_audit.Published, e => e is MarketingRoadmapItemCreatedAuditEvent);
    }

    [Fact]
    public async Task Create_Lazily_Creates_The_Product_Singleton()
    {
        await CreateHandler().HandleAsync(ValidCreate(), CancellationToken.None);

        Assert.True(await _db.MarketingProducts.AnyAsync(p => p.Id == MarketingProduct.SingletonId));
    }

    [Fact]
    public async Task Update_Persists_New_Values_And_Publishes_Audit()
    {
        var id = await SeedAsync("Original");

        var result = await UpdateHandler().HandleAsync(new UpdateMarketingRoadmapItemRequest(
            id, "Renamed", "New description", "chart-line", MarketingDeliveryStatus.Planned, 4), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var saved = await _db.MarketingRoadmapItems.SingleAsync(r => r.Id == id);
        Assert.Equal("Renamed", saved.Title);
        Assert.Equal("New description", saved.Description);
        Assert.Equal(4, saved.DisplayOrder);
        Assert.Equal(MarketingDeliveryStatus.Planned, saved.DeliveryStatus);
        Assert.Contains(_audit.Published, e => e is MarketingRoadmapItemUpdatedAuditEvent);
    }

    [Fact]
    public async Task Update_Returns_NotFound_For_Unknown_Id()
    {
        var result = await UpdateHandler().HandleAsync(new UpdateMarketingRoadmapItemRequest(
            Guid.NewGuid(), "Title", "Description", "icon", MarketingDeliveryStatus.Planned, 0), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task SetPublication_Publish_Then_Unpublish_Toggles_State()
    {
        var id = await SeedAsync("Item");

        var published = await PublicationHandler().HandleAsync(new SetMarketingRoadmapItemPublicationRequest(id, true), CancellationToken.None);
        Assert.True(published.IsSuccess);
        Assert.True(published.Value!.IsPublished);

        var unpublished = await PublicationHandler().HandleAsync(new SetMarketingRoadmapItemPublicationRequest(id, false), CancellationToken.None);
        Assert.True(unpublished.IsSuccess);
        Assert.False(unpublished.Value!.IsPublished);
    }

    [Fact]
    public async Task SetPublication_Returns_NotFound_For_Unknown_Id()
    {
        var result = await PublicationHandler().HandleAsync(
            new SetMarketingRoadmapItemPublicationRequest(Guid.NewGuid(), true), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task Unpublished_Roadmap_Item_Is_Excluded_From_GetMarketingContent()
    {
        await SeedAsync("Hidden item");

        var content = await new GetMarketingContentHandler(_db).HandleAsync(new GetMarketingContentRequest(), CancellationToken.None);

        Assert.True(content.IsSuccess);
        Assert.Empty(content.Value!.Roadmap);
    }

    [Fact]
    public async Task Reorder_Sets_DisplayOrder_By_Index()
    {
        var a = await SeedAsync("A", 5);
        var b = await SeedAsync("B", 6);
        var c = await SeedAsync("C", 7);

        var result = await ReorderHandler().HandleAsync(
            new ReorderMarketingRoadmapItemsRequest([b, c, a]), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(0, (await _db.MarketingRoadmapItems.SingleAsync(r => r.Id == b)).DisplayOrder);
        Assert.Equal(1, (await _db.MarketingRoadmapItems.SingleAsync(r => r.Id == c)).DisplayOrder);
        Assert.Equal(2, (await _db.MarketingRoadmapItems.SingleAsync(r => r.Id == a)).DisplayOrder);
        Assert.Contains(_audit.Published, e => e is MarketingRoadmapItemsReorderedAuditEvent);
    }

    [Fact]
    public async Task Reorder_Returns_NotFound_When_An_Id_Does_Not_Exist()
    {
        var a = await SeedAsync("A");

        var result = await ReorderHandler().HandleAsync(
            new ReorderMarketingRoadmapItemsRequest([a, Guid.NewGuid()]), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }
}
