using HR.Modules.Sickness.Domain;
using HR.Modules.Sickness.Persistence;
using HR.Modules.Sickness.Services;
using HR.Modules.Sickness.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Sickness.Tests;

public class SicknessCategoryDefaultsProvisionerTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 6, 8, 10, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task EnsureDefaultSicknessCategoriesAsync_Creates_Default_Set_When_None_Exist()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var provisioner = new SicknessCategoryDefaultsProvisioner(context, new FakeClock(FixedUtcNow));

        await provisioner.EnsureDefaultSicknessCategoriesAsync(companyId, CancellationToken.None);

        var names = await context.SicknessCategories.Where(c => c.CompanyId == companyId).Select(c => c.Name).ToListAsync();

        Assert.Equal(
            new[] { "Cold/Flu", "Back Pain", "Migraine" }.OrderBy(n => n),
            names.OrderBy(n => n));
    }

    [Fact]
    public async Task EnsureDefaultSicknessCategoriesAsync_Does_Nothing_When_Company_Already_Has_Categories()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        context.SicknessCategories.Add(SicknessCategory.Create(Guid.NewGuid(), companyId, "Custom Category", 1, now));
        await context.SaveChangesAsync();

        var provisioner = new SicknessCategoryDefaultsProvisioner(context, new FakeClock(FixedUtcNow));
        await provisioner.EnsureDefaultSicknessCategoriesAsync(companyId, CancellationToken.None);

        var category = await context.SicknessCategories.SingleAsync(c => c.CompanyId == companyId);
        Assert.Equal("Custom Category", category.Name);
    }

    private static SicknessDbContext BuildContext() =>
        new(new DbContextOptionsBuilder<SicknessDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString("N")).Options);
}
