using HR.Modules.Companies.Domain;
using HR.Modules.Companies.Features.GetPlatformSettings;
using HR.Modules.Companies.Persistence;
using HR.Modules.Companies.Tests.Infrastructure;

using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Companies.Tests;

public class GetPlatformSettingsHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 30, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task HandleAsync_Lazy_Seeds_Default_Row_When_None_Exists_And_Persists_It()
    {
        await using var context = BuildContext();
        var handler = new GetPlatformSettingsHandler(context, new FakeClock(Now.UtcDateTime));

        var result = await handler.HandleAsync(new GetPlatformSettingsRequest(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(14, result.Value!.TrialLengthDays);
        Assert.Equal(20.00m, result.Value.DefaultMonthlyPriceGbp);
        Assert.Equal("support@hrplatform.com", result.Value.SupportEmail);
        Assert.False(result.Value.MaintenanceModeEnabled);
        Assert.Null(result.Value.MaintenanceModeMessage);
        Assert.Empty(result.Value.FeatureFlags);
        Assert.Equal(Now, result.Value.UpdatedAt);
        Assert.Null(result.Value.UpdatedByUserId);

        var persisted = await context.PlatformSettings.SingleOrDefaultAsync(s => s.Id == PlatformSettings.SingletonId);
        Assert.NotNull(persisted);
    }

    [Fact]
    public async Task HandleAsync_Returns_Existing_Row_Values_When_One_Already_Exists()
    {
        await using var context = BuildContext();
        var settings = PlatformSettings.CreateDefault(Now);
        settings.Update(
            trialLengthDays: 21,
            defaultMonthlyPriceGbp: 19.99m,
            supportEmail: "existing@example.com",
            maintenanceModeEnabled: true,
            maintenanceModeMessage: "Scheduled maintenance",
            featureFlagsJson: "{\"beta\":true,\"gamma\":false}",
            updatedByUserId: Guid.NewGuid(),
            now: Now);
        context.PlatformSettings.Add(settings);
        await context.SaveChangesAsync();

        var handler = new GetPlatformSettingsHandler(context, new FakeClock(Now.AddDays(1).UtcDateTime));

        var result = await handler.HandleAsync(new GetPlatformSettingsRequest(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(21, result.Value!.TrialLengthDays);
        Assert.Equal(19.99m, result.Value.DefaultMonthlyPriceGbp);
        Assert.Equal("existing@example.com", result.Value.SupportEmail);
        Assert.True(result.Value.MaintenanceModeEnabled);
        Assert.Equal("Scheduled maintenance", result.Value.MaintenanceModeMessage);
        Assert.True(result.Value.FeatureFlags["beta"]);
        Assert.False(result.Value.FeatureFlags["gamma"]);

        var rowCount = await context.PlatformSettings.CountAsync();
        Assert.Equal(1, rowCount);
    }

    private static CompaniesDbContext BuildContext()
    {
        var options = new DbContextOptionsBuilder<CompaniesDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        return new CompaniesDbContext(options);
    }
}
