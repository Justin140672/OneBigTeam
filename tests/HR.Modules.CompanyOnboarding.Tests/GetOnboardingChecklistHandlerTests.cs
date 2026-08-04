using HR.Modules.CompanyOnboarding.Features.GetOnboardingChecklist;
using HR.Modules.CompanyOnboarding.Persistence;
using HR.Modules.CompanyOnboarding.Services;
using HR.Modules.CompanyOnboarding.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.CompanyOnboarding.Tests;

public class GetOnboardingChecklistHandlerTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 6, 30, 10, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task HandleAsync_Returns_Unauthorized_When_TenantId_Is_Null()
    {
        await using var db = BuildContext();
        var registry = new OnboardingTaskRegistry([]);
        var handler = new GetOnboardingChecklistHandler(db, registry, FakeCurrentTenant.None, new FakeClock(FixedUtcNow));

        var result = await handler.HandleAsync(CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("unauthorized", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_Unauthorized_When_TenantId_Is_Not_A_Guid()
    {
        await using var db = BuildContext();
        var registry = new OnboardingTaskRegistry([]);
        var handler = new GetOnboardingChecklistHandler(db, registry, FakeCurrentTenant.For("not-a-guid"), new FakeClock(FixedUtcNow));

        var result = await handler.HandleAsync(CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("unauthorized", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Computes_Completion_Percentage_For_Partially_Complete_Tasks()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var registry = new OnboardingTaskRegistry([
            new FakeOnboardingTaskDefinition("t1", 1, isCompleted: true),
            new FakeOnboardingTaskDefinition("t2", 2, isCompleted: true),
            new FakeOnboardingTaskDefinition("t3", 3, isCompleted: true),
            new FakeOnboardingTaskDefinition("t4", 4, isCompleted: false),
            new FakeOnboardingTaskDefinition("t5", 5, isCompleted: false),
            new FakeOnboardingTaskDefinition("t6", 6, isCompleted: false),
        ]);
        var handler = new GetOnboardingChecklistHandler(db, registry, FakeCurrentTenant.For(companyId), new FakeClock(FixedUtcNow));

        var result = await handler.HandleAsync(CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(50, result.Value!.CompletionPercentage);
        Assert.Equal(6, result.Value.Tasks.Count);
        Assert.False(result.Value.IsHidden);
    }

    [Fact]
    public async Task HandleAsync_Creates_Progress_Row_On_First_Call()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var registry = new OnboardingTaskRegistry([new FakeOnboardingTaskDefinition("t1", 1, isCompleted: false)]);
        var handler = new GetOnboardingChecklistHandler(db, registry, FakeCurrentTenant.For(companyId), new FakeClock(FixedUtcNow));

        Assert.Empty(db.Progress);

        var result = await handler.HandleAsync(CancellationToken.None);

        Assert.True(result.IsSuccess);
        var saved = await db.Progress.SingleAsync();
        Assert.Equal(companyId, saved.CompanyId);
    }

    [Fact]
    public async Task HandleAsync_Creates_TaskCompletion_Rows_For_Each_Registered_Task()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var registry = new OnboardingTaskRegistry([
            new FakeOnboardingTaskDefinition("t1", 1, isCompleted: true),
            new FakeOnboardingTaskDefinition("t2", 2, isCompleted: false),
        ]);
        var handler = new GetOnboardingChecklistHandler(db, registry, FakeCurrentTenant.For(companyId), new FakeClock(FixedUtcNow));

        await handler.HandleAsync(CancellationToken.None);

        var completions = await db.TaskCompletions.Where(t => t.CompanyId == companyId).ToListAsync();
        Assert.Equal(2, completions.Count);
        Assert.Contains(completions, c => c.TaskKey == "t1" && c.IsCompleted);
        Assert.Contains(completions, c => c.TaskKey == "t2" && !c.IsCompleted);
    }

    [Fact]
    public async Task HandleAsync_Marks_Progress_Completed_And_Hidden_Once_All_Mandatory_Tasks_Complete()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var registry = new OnboardingTaskRegistry([
            new FakeOnboardingTaskDefinition("t1", 1, isCompleted: true),
            new FakeOnboardingTaskDefinition("t2", 2, isCompleted: true),
        ]);
        var handler = new GetOnboardingChecklistHandler(db, registry, FakeCurrentTenant.For(companyId), new FakeClock(FixedUtcNow));

        var result = await handler.HandleAsync(CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(100, result.Value!.CompletionPercentage);
        Assert.True(result.Value.IsHidden);

        var progress = await db.Progress.SingleAsync(p => p.CompanyId == companyId);
        Assert.NotNull(progress.CompletedAt);
        Assert.True(progress.IsHidden);
    }

    private static CompanyOnboardingDbContext BuildContext()
    {
        var options = new DbContextOptionsBuilder<CompanyOnboardingDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new CompanyOnboardingDbContext(options);
    }
}
