using HR.Modules.Marketing.Domain;
using HR.Modules.Marketing.Features.CreateMarketingFeature;
using HR.Modules.Marketing.Features.CreateMarketingRoadmapItem;
using HR.Modules.Marketing.Features.GetMarketingContent;
using HR.Modules.Marketing.Features.SetMarketingFeaturePublication;
using HR.Modules.Marketing.Features.SetMarketingRoadmapItemPublication;
using HR.Modules.Marketing.Persistence;
using HR.Modules.Marketing.Tests.Infrastructure;

namespace HR.Modules.Marketing.Tests;

public class GetMarketingContentHandlerTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 6, 30, 10, 0, 0, DateTimeKind.Utc);

    private readonly MarketingDbContext _db = MarketingTestContext.Build();
    private readonly FakeAuditEventPublisher _audit = new();
    private readonly Guid _actor = Guid.NewGuid();

    private CreateMarketingFeatureHandler CreateFeature() =>
        new(_db, new FakeCurrentUser(_actor), new FakeClock(FixedUtcNow), _audit);

    private SetMarketingFeaturePublicationHandler PublishFeature() =>
        new(_db, new FakeCurrentUser(_actor), new FakeClock(FixedUtcNow), _audit);

    private CreateMarketingRoadmapItemHandler CreateRoadmap() =>
        new(_db, new FakeCurrentUser(_actor), new FakeClock(FixedUtcNow), _audit);

    private SetMarketingRoadmapItemPublicationHandler PublishRoadmap() =>
        new(_db, new FakeCurrentUser(_actor), new FakeClock(FixedUtcNow), _audit);

    private GetMarketingContentHandler GetHandler() => new(_db);

    private async Task<Guid> AddFeatureAsync(string slug, string title, int order, bool publish,
        MarketingDeliveryStatus status = MarketingDeliveryStatus.Available, IReadOnlyList<string>? benefits = null)
    {
        var created = await CreateFeature().HandleAsync(new CreateMarketingFeatureRequest(
            slug, "users", title, "Summary", "Intro", "Detail", benefits ?? ["b1", "b2"], "yt123", order, status),
            CancellationToken.None);
        Assert.True(created.IsSuccess);
        if (publish)
        {
            await PublishFeature().HandleAsync(new SetMarketingFeaturePublicationRequest(created.Value!.Id, true), CancellationToken.None);
        }

        return created.Value!.Id;
    }

    private async Task AddRoadmapAsync(string title, int order, bool publish)
    {
        var created = await CreateRoadmap().HandleAsync(new CreateMarketingRoadmapItemRequest(
            title, "Description", "chart-line", MarketingDeliveryStatus.ComingSoon, order), CancellationToken.None);
        Assert.True(created.IsSuccess);
        if (publish)
        {
            await PublishRoadmap().HandleAsync(new SetMarketingRoadmapItemPublicationRequest(created.Value!.Id, true), CancellationToken.None);
        }
    }

    [Fact]
    public async Task Returns_Only_Published_Features_And_Roadmap_Items()
    {
        await AddFeatureAsync("published-feature", "Published", 0, publish: true);
        await AddFeatureAsync("draft-feature", "Draft", 1, publish: false);
        await AddRoadmapAsync("Published item", 0, publish: true);
        await AddRoadmapAsync("Draft item", 1, publish: false);

        var result = await GetHandler().HandleAsync(new GetMarketingContentRequest(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(new[] { "published-feature" }, result.Value!.Features.Select(f => f.Slug));
        Assert.Equal(new[] { "Published item" }, result.Value.Roadmap.Select(r => r.Title));
    }

    [Fact]
    public async Task Features_Are_Ordered_By_DisplayOrder_Then_Title()
    {
        await AddFeatureAsync("c", "Charlie", 2, publish: true);
        await AddFeatureAsync("a", "Alpha", 0, publish: true);
        await AddFeatureAsync("b-second", "Bravo two", 1, publish: true);
        await AddFeatureAsync("b-first", "Bravo one", 1, publish: true);

        var result = await GetHandler().HandleAsync(new GetMarketingContentRequest(), CancellationToken.None);

        Assert.Equal(
            new[] { "Alpha", "Bravo one", "Bravo two", "Charlie" },
            result.Value!.Features.Select(f => f.Title));
    }

    [Fact]
    public async Task Maps_Feature_Dto_Including_Benefits_And_DeliveryStatus_String()
    {
        await AddFeatureAsync("mapped", "Mapped", 0, publish: true,
            status: MarketingDeliveryStatus.ComingSoon, benefits: ["First benefit", "Second benefit"]);

        var result = await GetHandler().HandleAsync(new GetMarketingContentRequest(), CancellationToken.None);

        var dto = Assert.Single(result.Value!.Features);
        Assert.Equal("mapped", dto.Slug);
        Assert.Equal("Mapped", dto.Title);
        Assert.Equal("Detail", dto.DetailedContent);
        Assert.Equal("yt123", dto.YouTubeId);
        Assert.Equal(new[] { "First benefit", "Second benefit" }, dto.Benefits);
        Assert.Equal("ComingSoon", dto.DeliveryStatus);
    }

    [Fact]
    public async Task Returns_Default_Product_Name_When_No_Product_Row()
    {
        var result = await GetHandler().HandleAsync(new GetMarketingContentRequest(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("One Big Team", result.Value!.Product.Name);
        Assert.Empty(result.Value.Features);
        Assert.Empty(result.Value.Roadmap);
    }
}
