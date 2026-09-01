using HR.Modules.Companies.Domain;
using HR.Modules.Companies.Features.GetPublicSubscriptionPricing;
using HR.Modules.Companies.Features.GetSubscriptionPricingConfig;
using HR.Modules.Companies.Features.UpdateSubscriptionPricingConfig;
using HR.Modules.Companies.Persistence;
using HR.Modules.Companies.Tests.Infrastructure;
using HR.SharedKernel.Pricing;

using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Companies.Tests;

public class SubscriptionPricingConfigHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 31, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Get_LazySeeds_Default_When_No_Row_Exists()
    {
        await using var context = BuildContext();
        var handler = new GetSubscriptionPricingConfigHandler(context, new FakeClock(Now.UtcDateTime));

        var result = await handler.HandleAsync(new GetSubscriptionPricingConfigRequest(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(3, result.Value!.Bands.Count);
        Assert.Equal(20.00m, result.Value.MinimumMonthlyChargeGbp);
        Assert.Equal(1, result.Value.Bands[0].StartEmployee);
        Assert.Null(result.Value.Bands[^1].EndEmployee);
        Assert.Equal(1, await context.PlatformSettings.CountAsync());
    }

    [Fact]
    public async Task Update_Persists_Valid_Config_And_Publishes_Audit_Event()
    {
        await using var context = BuildContext();
        var audit = new CapturingAuditEventPublisher();
        var userId = Guid.NewGuid();
        var handler = new UpdateSubscriptionPricingConfigHandler(
            context, new FakeCurrentUser(userId), new FakeClock(Now.UtcDateTime), audit);

        var request = new UpdateSubscriptionPricingConfigRequest(
            new[]
            {
                new UpdateSubscriptionPricingBandInput(1, 100, 3.00m),
                new UpdateSubscriptionPricingBandInput(101, null, 2.00m),
            },
            25.00m);

        var result = await handler.HandleAsync(request, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value!.Bands.Count);
        Assert.Equal(25.00m, result.Value.MinimumMonthlyChargeGbp);
        Assert.Single(audit.Published);
        Assert.IsType<SubscriptionPricingConfigUpdatedAuditEvent>(audit.Published[0]);

        var persisted = await context.PlatformSettings.SingleAsync();
        var config = persisted.GetPricingConfig();
        Assert.Equal(25.00m, config.MinimumMonthlyChargeGbp);
        Assert.Equal(3.00m, config.Bands[0].PricePerEmployee);
        Assert.Equal(userId, persisted.UpdatedByUserId);
    }

    [Fact]
    public async Task Update_Rejects_Structurally_Invalid_Config_And_Mutates_Nothing()
    {
        await using var context = BuildContext();
        var audit = new CapturingAuditEventPublisher();
        var handler = new UpdateSubscriptionPricingConfigHandler(
            context, new FakeCurrentUser(Guid.NewGuid()), new FakeClock(Now.UtcDateTime), audit);

        // Gap between band 1 (ends 50) and band 2 (starts 60).
        var request = new UpdateSubscriptionPricingConfigRequest(
            new[]
            {
                new UpdateSubscriptionPricingBandInput(1, 50, 3.00m),
                new UpdateSubscriptionPricingBandInput(60, null, 2.00m),
            },
            25.00m);

        var result = await handler.HandleAsync(request, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Empty(audit.Published);
    }

    [Fact]
    public async Task GetPublic_Returns_Default_When_Not_Seeded_And_Never_Seeds()
    {
        await using var context = BuildContext();
        var handler = new GetPublicSubscriptionPricingHandler(context);

        var result = await handler.HandleAsync(new GetPublicSubscriptionPricingRequest(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(3, result.Value!.Bands.Count);
        Assert.Equal(20.00m, result.Value.MinimumMonthlyChargeGbp);
        Assert.Equal(0, await context.PlatformSettings.CountAsync());
    }

    [Fact]
    public async Task GetPublic_Returns_Persisted_Config_When_Seeded()
    {
        await using var context = BuildContext();
        var settings = PlatformSettings.CreateDefault(Now);
        settings.UpdatePricingConfig(
            new SubscriptionPricingConfig(
                new[] { new SubscriptionPricingBand(1, null, 7.50m) }, 99.00m),
            Guid.NewGuid(),
            Now);
        context.PlatformSettings.Add(settings);
        await context.SaveChangesAsync();

        var handler = new GetPublicSubscriptionPricingHandler(context);
        var result = await handler.HandleAsync(new GetPublicSubscriptionPricingRequest(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value!.Bands);
        Assert.Equal(99.00m, result.Value.MinimumMonthlyChargeGbp);
    }

    [Theory]
    [InlineData(1, 50, 2)]   // first band not starting at 1 handled elsewhere; here valid-ish shape
    public void PlatformSettings_UpdatePricingConfig_Rejects_Invalid(int start, int end, int _)
    {
        var settings = PlatformSettings.CreateDefault(Now);

        // Overlap: band 2 starts before band 1 ends.
        var invalid = new SubscriptionPricingConfig(
            new[]
            {
                new SubscriptionPricingBand(start, end, 2m),
                new SubscriptionPricingBand(end - 5, null, 1m),
            },
            10m);

        var result = settings.UpdatePricingConfig(invalid, null, Now);

        Assert.True(result.IsFailure);
    }

    private static CompaniesDbContext BuildContext()
    {
        var options = new DbContextOptionsBuilder<CompaniesDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        return new CompaniesDbContext(options);
    }
}
