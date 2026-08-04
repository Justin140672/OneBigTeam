using HR.Modules.CompanyOnboarding.Features.DismissOnboardingChecklist;
using HR.Modules.CompanyOnboarding.Persistence;
using HR.Modules.CompanyOnboarding.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.CompanyOnboarding.Tests;

public class DismissOnboardingChecklistHandlerTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 6, 30, 10, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task HandleAsync_Returns_Unauthorized_When_TenantId_Is_Null()
    {
        await using var db = BuildContext();
        var handler = new DismissOnboardingChecklistHandler(db, FakeCurrentTenant.None, new FakeClock(FixedUtcNow));

        var result = await handler.HandleAsync(CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("unauthorized", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_Unauthorized_When_TenantId_Is_Not_A_Guid()
    {
        await using var db = BuildContext();
        var handler = new DismissOnboardingChecklistHandler(db, FakeCurrentTenant.For("not-a-guid"), new FakeClock(FixedUtcNow));

        var result = await handler.HandleAsync(CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("unauthorized", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Creates_Progress_Row_If_None_Exists_And_Marks_Dismissed()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var handler = new DismissOnboardingChecklistHandler(db, FakeCurrentTenant.For(companyId), new FakeClock(FixedUtcNow));

        Assert.Empty(db.Progress);

        var result = await handler.HandleAsync(CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value!.IsHidden);

        var saved = await db.Progress.SingleAsync();
        Assert.Equal(companyId, saved.CompanyId);
        Assert.True(saved.IsDismissedEarly);
        Assert.True(saved.IsHidden);
    }

    [Fact]
    public async Task HandleAsync_Sets_IsDismissedEarly_And_IsHidden_On_Existing_Progress()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        db.Progress.Add(HR.Modules.CompanyOnboarding.Domain.CompanyOnboardingProgress.Create(companyId, new DateTimeOffset(FixedUtcNow, TimeSpan.Zero)));
        await db.SaveChangesAsync();

        var handler = new DismissOnboardingChecklistHandler(db, FakeCurrentTenant.For(companyId), new FakeClock(FixedUtcNow.AddDays(1)));

        var result = await handler.HandleAsync(CancellationToken.None);

        Assert.True(result.IsSuccess);
        var saved = await db.Progress.SingleAsync(p => p.CompanyId == companyId);
        Assert.True(saved.IsDismissedEarly);
        Assert.True(saved.IsHidden);
    }

    private static CompanyOnboardingDbContext BuildContext()
    {
        var options = new DbContextOptionsBuilder<CompanyOnboardingDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new CompanyOnboardingDbContext(options);
    }
}
