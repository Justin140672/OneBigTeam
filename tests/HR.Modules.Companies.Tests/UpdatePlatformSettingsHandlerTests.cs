using HR.Modules.Companies.Domain;
using HR.Modules.Companies.Features.UpdatePlatformSettings;
using HR.Modules.Companies.Persistence;
using HR.Modules.Companies.Tests.Infrastructure;

using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Companies.Tests;

public class UpdatePlatformSettingsHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 30, 10, 0, 0, TimeSpan.Zero);

    private static UpdatePlatformSettingsRequest ValidRequest() => new(
        TrialLengthDays: 30,
        DefaultMonthlyPriceGbp: 24.99m,
        SupportEmail: "help@example.com",
        MaintenanceModeEnabled: true,
        MaintenanceModeMessage: "Undergoing maintenance",
        FeatureFlags: new Dictionary<string, bool> { ["beta"] = true });

    [Fact]
    public async Task HandleAsync_Persists_New_Values_Publishes_Audit_Event_And_Sets_UpdatedByUserId()
    {
        await using var context = BuildContext();
        var existing = PlatformSettings.CreateDefault(Now);
        context.PlatformSettings.Add(existing);
        await context.SaveChangesAsync();

        var actorId = Guid.NewGuid();
        var publisher = new CapturingAuditEventPublisher();
        var handler = BuildHandler(context, actorId, publisher, Now.AddDays(1));

        var result = await handler.HandleAsync(ValidRequest(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(30, result.Value!.TrialLengthDays);
        Assert.Equal(24.99m, result.Value.DefaultMonthlyPriceGbp);
        Assert.Equal("help@example.com", result.Value.SupportEmail);
        Assert.True(result.Value.MaintenanceModeEnabled);
        Assert.Equal("Undergoing maintenance", result.Value.MaintenanceModeMessage);
        Assert.True(result.Value.FeatureFlags["beta"]);
        Assert.Equal(actorId, result.Value.UpdatedByUserId);
        Assert.Equal(Now.AddDays(1), result.Value.UpdatedAt);

        var persisted = await context.PlatformSettings.SingleAsync(s => s.Id == PlatformSettings.SingletonId);
        Assert.Equal(30, persisted.TrialLengthDays);
        Assert.Equal("help@example.com", persisted.SupportEmail);
        Assert.Equal(actorId, persisted.UpdatedByUserId);

        var published = Assert.Single(publisher.Published);
        var auditEvent = Assert.IsType<PlatformSettingsUpdatedAuditEvent>(published);
        Assert.Equal(PlatformSettings.SingletonId, auditEvent.SettingsId);
        Assert.Equal(actorId, auditEvent.ActorUserId);
        Assert.Equal(Now.AddDays(1), auditEvent.OccurredAt);
        Assert.NotNull(auditEvent.PreviousState);
        Assert.Equal(14, auditEvent.PreviousState!.TrialLengthDays);
        Assert.Equal("support@hrplatform.com", auditEvent.PreviousState.SupportEmail);
        Assert.Equal(30, auditEvent.CurrentState.TrialLengthDays);
        Assert.Equal("help@example.com", auditEvent.CurrentState.SupportEmail);
    }

    [Fact]
    public async Task HandleAsync_Lazy_Seeds_Missing_Row_Then_Updates_It()
    {
        await using var context = BuildContext();
        var actorId = Guid.NewGuid();
        var publisher = new CapturingAuditEventPublisher();
        var handler = BuildHandler(context, actorId, publisher, Now);

        var result = await handler.HandleAsync(ValidRequest(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var rowCount = await context.PlatformSettings.CountAsync();
        Assert.Equal(1, rowCount);

        var persisted = await context.PlatformSettings.SingleAsync(s => s.Id == PlatformSettings.SingletonId);
        Assert.Equal(30, persisted.TrialLengthDays);

        var published = Assert.Single(publisher.Published);
        var auditEvent = Assert.IsType<PlatformSettingsUpdatedAuditEvent>(published);
        // Lazily-seeded row starts from CreateDefault defaults as the "previous" snapshot.
        Assert.Equal(14, auditEvent.PreviousState!.TrialLengthDays);
    }

    [Fact]
    public async Task HandleAsync_Returns_Failure_And_Does_Not_Save_Or_Publish_When_Validation_Fails()
    {
        await using var context = BuildContext();
        var existing = PlatformSettings.CreateDefault(Now);
        context.PlatformSettings.Add(existing);
        await context.SaveChangesAsync();

        var publisher = new CapturingAuditEventPublisher();
        var handler = BuildHandler(context, Guid.NewGuid(), publisher, Now.AddDays(1));

        var invalidRequest = ValidRequest() with { TrialLengthDays = 0 };

        var result = await handler.HandleAsync(invalidRequest, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("validation", result.Error.Code);
        Assert.Empty(publisher.Published);

        var persisted = await context.PlatformSettings.SingleAsync(s => s.Id == PlatformSettings.SingletonId);
        Assert.Equal(14, persisted.TrialLengthDays);
        Assert.Equal("support@hrplatform.com", persisted.SupportEmail);
    }

    private static UpdatePlatformSettingsHandler BuildHandler(
        CompaniesDbContext context,
        Guid actorId,
        HR.SharedKernel.IAuditEventPublisher publisher,
        DateTimeOffset now)
    {
        return new UpdatePlatformSettingsHandler(
            context,
            new FakeCurrentUser(actorId, email: "admin@example.com"),
            new FakeClock(now.UtcDateTime),
            publisher);
    }

    private static CompaniesDbContext BuildContext()
    {
        var options = new DbContextOptionsBuilder<CompaniesDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        return new CompaniesDbContext(options);
    }
}
