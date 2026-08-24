using HR.Modules.Sickness.Features.CreateSicknessCategory;
using HR.Modules.Sickness.Persistence;
using HR.Modules.Sickness.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Sickness.Tests;

public class CreateSicknessCategoryHandlerTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 6, 30, 10, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task HandleAsync_Creates_SicknessCategory()
    {
        await using var db = BuildContext();
        var handler = new CreateSicknessCategoryHandler(db, new FakeClock(FixedUtcNow), new FakeAuditEventPublisher());
        var companyId = Guid.NewGuid();

        var result = await handler.HandleAsync(new CreateSicknessCategoryRequest
        {
            CompanyId = companyId,
            Name = "Cold",
            DisplayOrder = 1
        }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        Assert.NotEqual(Guid.Empty, result.Value!.Id);
        Assert.Equal(companyId, result.Value.CompanyId);
        Assert.Equal("Cold", result.Value.Name);
        Assert.Equal(1, result.Value.DisplayOrder);
        Assert.True(result.Value.IsActive);

        var saved = await db.SicknessCategories.SingleAsync();
        Assert.Equal("Cold", saved.Name);
        Assert.Equal(companyId, saved.CompanyId);
    }

    [Fact]
    public async Task HandleAsync_Sets_CreatedAt_And_UpdatedAt()
    {
        await using var db = BuildContext();
        var handler = new CreateSicknessCategoryHandler(db, new FakeClock(FixedUtcNow), new FakeAuditEventPublisher());

        var result = await handler.HandleAsync(new CreateSicknessCategoryRequest
        {
            CompanyId = Guid.NewGuid(),
            Name = "Flu",
            DisplayOrder = 2
        }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(new DateTimeOffset(FixedUtcNow, TimeSpan.Zero), result.Value!.CreatedAt);
        Assert.Equal(new DateTimeOffset(FixedUtcNow, TimeSpan.Zero), result.Value.UpdatedAt);
    }

    [Fact]
    public async Task HandleAsync_Succeeds_When_DisplayOrder_Is_Zero()
    {
        await using var db = BuildContext();
        var handler = new CreateSicknessCategoryHandler(db, new FakeClock(FixedUtcNow), new FakeAuditEventPublisher());
        var companyId = Guid.NewGuid();

        var result = await handler.HandleAsync(new CreateSicknessCategoryRequest
        {
            CompanyId = companyId,
            Name = "Cold",
            DisplayOrder = 0
        }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(0, result.Value!.DisplayOrder);

        var saved = await db.SicknessCategories.SingleAsync();
        Assert.Equal(0, saved.DisplayOrder);
    }

    [Fact]
    public async Task HandleAsync_Returns_Conflict_When_Name_Already_Exists()
    {
        await using var db = BuildContext();
        var handler = new CreateSicknessCategoryHandler(db, new FakeClock(FixedUtcNow), new FakeAuditEventPublisher());
        var companyId = Guid.NewGuid();

        await handler.HandleAsync(new CreateSicknessCategoryRequest
        {
            CompanyId = companyId,
            Name = "Cold",
            DisplayOrder = 1
        }, CancellationToken.None);

        var result = await handler.HandleAsync(new CreateSicknessCategoryRequest
        {
            CompanyId = companyId,
            Name = "Cold",
            DisplayOrder = 2
        }, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("conflict", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Allows_Same_Name_In_Different_Companies()
    {
        await using var db = BuildContext();
        var handler = new CreateSicknessCategoryHandler(db, new FakeClock(FixedUtcNow), new FakeAuditEventPublisher());

        await handler.HandleAsync(new CreateSicknessCategoryRequest
        {
            CompanyId = Guid.NewGuid(),
            Name = "Cold",
            DisplayOrder = 1
        }, CancellationToken.None);

        var result = await handler.HandleAsync(new CreateSicknessCategoryRequest
        {
            CompanyId = Guid.NewGuid(),
            Name = "Cold",
            DisplayOrder = 1
        }, CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    // SICK-06: category CRUD previously had no audit trail at all; actor is resolved server-side
    // from the authenticated caller (threaded via CreateSicknessCategoryRequest.ActorEmployeeId).
    [Fact]
    public async Task HandleAsync_Publishes_Audit_Event_With_ActorEmployeeId_And_Structured_Payload()
    {
        await using var db = BuildContext();
        var auditPublisher = new FakeAuditEventPublisher();
        var actorId = Guid.NewGuid();
        var companyId = Guid.NewGuid();
        var handler = new CreateSicknessCategoryHandler(db, new FakeClock(FixedUtcNow), auditPublisher);

        var result = await handler.HandleAsync(new CreateSicknessCategoryRequest
        {
            CompanyId = companyId,
            Name = "Cold",
            DisplayOrder = 1,
            ActorEmployeeId = actorId
        }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        var auditEvent = Assert.IsType<SicknessCategoryCreatedAuditEvent>(Assert.Single(auditPublisher.PublishedEvents));
        Assert.Equal(companyId, auditEvent.CompanyId);
        Assert.Equal(result.Value!.Id, auditEvent.CategoryId);
        Assert.Equal(actorId, ((HR.SharedKernel.IAuditEvent)auditEvent).ActorEmployeeId);
        Assert.Equal("Cold", auditEvent.Name);
        Assert.Equal(1, auditEvent.DisplayOrder);
    }

    [Fact]
    public async Task HandleAsync_Does_Not_Publish_Audit_Event_On_Conflict()
    {
        await using var db = BuildContext();
        var auditPublisher = new FakeAuditEventPublisher();
        var companyId = Guid.NewGuid();
        var handler = new CreateSicknessCategoryHandler(db, new FakeClock(FixedUtcNow), auditPublisher);

        await handler.HandleAsync(new CreateSicknessCategoryRequest
        {
            CompanyId = companyId,
            Name = "Cold",
            DisplayOrder = 1
        }, CancellationToken.None);

        auditPublisher.PublishedEvents.Clear();

        var result = await handler.HandleAsync(new CreateSicknessCategoryRequest
        {
            CompanyId = companyId,
            Name = "Cold",
            DisplayOrder = 2
        }, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Empty(auditPublisher.PublishedEvents);
    }

    private static SicknessDbContext BuildContext()
    {
        var options = new DbContextOptionsBuilder<SicknessDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new SicknessDbContext(options);
    }
}
