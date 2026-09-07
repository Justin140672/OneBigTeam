using HR.Modules.Marketing.Domain;
using HR.Modules.Marketing.Features.CreateMarketingFeature;
using HR.Modules.Marketing.Features.GetMarketingContent;
using HR.Modules.Marketing.Features.ReorderMarketingFeatures;
using HR.Modules.Marketing.Features.SetMarketingFeaturePublication;
using HR.Modules.Marketing.Features.UpdateMarketingFeature;
using HR.Modules.Marketing.Persistence;
using HR.Modules.Marketing.Tests.Infrastructure;

using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Marketing.Tests;

public class MarketingFeatureHandlerTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 6, 30, 10, 0, 0, DateTimeKind.Utc);

    private readonly MarketingDbContext _db = MarketingTestContext.Build();
    private readonly FakeAuditEventPublisher _audit = new();
    private readonly Guid _actor = Guid.NewGuid();

    private CreateMarketingFeatureHandler CreateHandler() =>
        new(_db, new FakeCurrentUser(_actor), new FakeClock(FixedUtcNow), _audit);

    private UpdateMarketingFeatureHandler UpdateHandler() =>
        new(_db, new FakeCurrentUser(_actor), new FakeClock(FixedUtcNow), _audit);

    private SetMarketingFeaturePublicationHandler PublicationHandler() =>
        new(_db, new FakeCurrentUser(_actor), new FakeClock(FixedUtcNow), _audit);

    private ReorderMarketingFeaturesHandler ReorderHandler() =>
        new(_db, new FakeCurrentUser(_actor), new FakeClock(FixedUtcNow), _audit);

    private GetMarketingContentHandler GetHandler() => new(_db);

    private static CreateMarketingFeatureRequest ValidCreate(string slug = "employee-management") => new(
        Slug: slug,
        IconName: "users",
        Title: "Employee Management",
        Summary: "Store employee information in one place.",
        Intro: "Keep records organised.",
        DetailedContent: null,
        Benefits: ["Records", "History"],
        YouTubeId: null,
        DisplayOrder: 0,
        DeliveryStatus: MarketingDeliveryStatus.Available);

    private async Task<Guid> SeedFeatureAsync(string slug, int order = 0)
    {
        var result = await CreateHandler().HandleAsync(ValidCreate(slug) with { DisplayOrder = order }, CancellationToken.None);
        Assert.True(result.IsSuccess);
        return result.Value!.Id;
    }

    // ── Create ───────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Create_Persists_Fields_Starts_Unpublished_And_Publishes_Audit()
    {
        var result = await CreateHandler().HandleAsync(ValidCreate(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value!.IsPublished);

        var saved = await _db.MarketingFeatures.SingleAsync();
        Assert.Equal("employee-management", saved.Slug);
        Assert.Equal("Employee Management", saved.Title);
        Assert.Equal(new[] { "Records", "History" }, saved.Benefits);
        Assert.Equal(MarketingDeliveryStatus.Available, saved.DeliveryStatus);
        Assert.Equal(_actor, saved.CreatedByUserId);
        Assert.False(saved.IsPublished);

        var published = Assert.Single(_audit.Published);
        Assert.IsType<MarketingFeatureCreatedAuditEvent>(published);
    }

    [Fact]
    public async Task Create_Lazily_Creates_The_Product_Singleton()
    {
        await CreateHandler().HandleAsync(ValidCreate(), CancellationToken.None);

        Assert.True(await _db.MarketingProducts.AnyAsync(p => p.Id == MarketingProduct.SingletonId));
    }

    [Fact]
    public async Task Create_Returns_Conflict_For_Duplicate_Slug_Case_Insensitive()
    {
        await SeedFeatureAsync("employee-management");

        var result = await CreateHandler().HandleAsync(ValidCreate("EMPLOYEE-MANAGEMENT"), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("conflict", result.Error.Code);
    }

    [Fact]
    public async Task Create_Returns_Failure_For_Invalid_Slug()
    {
        var result = await CreateHandler().HandleAsync(ValidCreate("Bad Slug"), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("validation", result.Error.Code);
    }

    // ── Update ───────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Update_Persists_New_Values_And_Publishes_Audit()
    {
        var id = await SeedFeatureAsync("employee-management");
        _audit.Published.ToList().Clear();

        var result = await UpdateHandler().HandleAsync(new UpdateMarketingFeatureRequest(
            id, "people-management", "users", "People Management", "New summary", "New intro",
            null, ["A"], null, 3, MarketingDeliveryStatus.ComingSoon), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var saved = await _db.MarketingFeatures.SingleAsync(f => f.Id == id);
        Assert.Equal("people-management", saved.Slug);
        Assert.Equal("People Management", saved.Title);
        Assert.Equal(3, saved.DisplayOrder);
        Assert.Equal(MarketingDeliveryStatus.ComingSoon, saved.DeliveryStatus);
        Assert.Contains(_audit.Published, e => e is MarketingFeatureUpdatedAuditEvent);
    }

    [Fact]
    public async Task Update_Returns_NotFound_For_Unknown_Id()
    {
        var result = await UpdateHandler().HandleAsync(new UpdateMarketingFeatureRequest(
            Guid.NewGuid(), "slug", "icon", "Title", "Summary", "Intro",
            null, null, null, 0, MarketingDeliveryStatus.Available), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task Update_Returns_Conflict_When_Slug_Belongs_To_Another_Feature()
    {
        await SeedFeatureAsync("leave-management", order: 1);
        var id = await SeedFeatureAsync("employee-management", order: 0);

        var result = await UpdateHandler().HandleAsync(new UpdateMarketingFeatureRequest(
            id, "LEAVE-MANAGEMENT", "icon", "Title", "Summary", "Intro",
            null, null, null, 0, MarketingDeliveryStatus.Available), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("conflict", result.Error.Code);
    }

    [Fact]
    public async Task Update_Allows_Keeping_Its_Own_Slug()
    {
        var id = await SeedFeatureAsync("employee-management");

        var result = await UpdateHandler().HandleAsync(new UpdateMarketingFeatureRequest(
            id, "employee-management", "icon", "Renamed", "Summary", "Intro",
            null, null, null, 0, MarketingDeliveryStatus.Available), CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    // ── Publication toggle ───────────────────────────────────────────────────────

    [Fact]
    public async Task SetPublication_Publish_Sets_IsPublished_True_Regardless_Of_DeliveryStatus()
    {
        var result = await CreateHandler().HandleAsync(
            ValidCreate() with { DeliveryStatus = MarketingDeliveryStatus.ComingSoon }, CancellationToken.None);
        var id = result.Value!.Id;

        var toggled = await PublicationHandler().HandleAsync(
            new SetMarketingFeaturePublicationRequest(id, true), CancellationToken.None);

        Assert.True(toggled.IsSuccess);
        Assert.True(toggled.Value!.IsPublished);
        var saved = await _db.MarketingFeatures.SingleAsync(f => f.Id == id);
        Assert.True(saved.IsPublished);
        Assert.Equal(MarketingDeliveryStatus.ComingSoon, saved.DeliveryStatus);
    }

    [Fact]
    public async Task SetPublication_Unpublish_Sets_IsPublished_False()
    {
        var id = await SeedFeatureAsync("employee-management");
        await PublicationHandler().HandleAsync(new SetMarketingFeaturePublicationRequest(id, true), CancellationToken.None);

        var result = await PublicationHandler().HandleAsync(
            new SetMarketingFeaturePublicationRequest(id, false), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value!.IsPublished);
    }

    [Fact]
    public async Task SetPublication_Returns_NotFound_For_Unknown_Id()
    {
        var result = await PublicationHandler().HandleAsync(
            new SetMarketingFeaturePublicationRequest(Guid.NewGuid(), true), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task Unpublished_Feature_With_Available_DeliveryStatus_Is_Excluded_From_GetMarketingContent()
    {
        // DeliveryStatus.Available but never published => must not surface publicly.
        await SeedFeatureAsync("employee-management");

        var content = await GetHandler().HandleAsync(new GetMarketingContentRequest(), CancellationToken.None);

        Assert.True(content.IsSuccess);
        Assert.Empty(content.Value!.Features);
    }

    // ── Reorder ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Reorder_Sets_DisplayOrder_By_Index()
    {
        var a = await SeedFeatureAsync("a-feature", order: 5);
        var b = await SeedFeatureAsync("b-feature", order: 6);
        var c = await SeedFeatureAsync("c-feature", order: 7);

        var result = await ReorderHandler().HandleAsync(
            new ReorderMarketingFeaturesRequest([c, a, b]), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(0, (await _db.MarketingFeatures.SingleAsync(f => f.Id == c)).DisplayOrder);
        Assert.Equal(1, (await _db.MarketingFeatures.SingleAsync(f => f.Id == a)).DisplayOrder);
        Assert.Equal(2, (await _db.MarketingFeatures.SingleAsync(f => f.Id == b)).DisplayOrder);
        Assert.Contains(_audit.Published, e => e is MarketingFeaturesReorderedAuditEvent);
    }

    [Fact]
    public async Task Reorder_Returns_NotFound_When_An_Id_Does_Not_Exist()
    {
        var a = await SeedFeatureAsync("a-feature");

        var result = await ReorderHandler().HandleAsync(
            new ReorderMarketingFeaturesRequest([a, Guid.NewGuid()]), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }
}
