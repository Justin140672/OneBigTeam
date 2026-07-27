using HR.Infrastructure.Abstractions;
using HR.Modules.Offboarding.Domain;
using HR.Modules.Offboarding.Features.StartOffboarding;
using HR.Modules.Offboarding.Persistence;
using HR.Modules.Offboarding.Services;
using HR.Modules.Offboarding.Tests.Infrastructure;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Offboarding.Tests;

public class OffboardingHistoryReplayerTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 25, 10, 0, 0, TimeSpan.Zero);

    private static OffboardingDbContext BuildContext() =>
        new(new DbContextOptionsBuilder<OffboardingDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);

    private static OffboardingPlan AddPlan(
        OffboardingDbContext dbContext,
        Guid companyId,
        Guid employeeId,
        DateTimeOffset createdAt,
        Action<OffboardingPlan>? mutate = null)
    {
        var plan = OffboardingPlan.Create(
            Guid.NewGuid(), companyId, employeeId, new DateOnly(2026, 8, 1), null, createdAt);
        mutate?.Invoke(plan);
        dbContext.OffboardingPlans.Add(plan);
        return plan;
    }

    [Fact]
    public async Task ReplayStartedOffboardingsAsync_Publishes_One_Event_Per_Plan_Regardless_Of_Status()
    {
        await using var dbContext = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId1 = Guid.NewGuid();
        var employeeId2 = Guid.NewGuid();

        var createdAt1 = Now.AddDays(-30);
        var createdAt2 = Now.AddDays(-10);

        AddPlan(dbContext, companyId, employeeId1, createdAt1, p => p.Start(createdAt1));
        AddPlan(dbContext, companyId, employeeId2, createdAt2, p =>
        {
            p.Start(createdAt2);
            p.Complete(Now.AddDays(-5));
        });
        await dbContext.SaveChangesAsync();

        var publisher = new CapturingIntegrationEventPublisher();
        var replayer = new OffboardingHistoryReplayer(dbContext, publisher);

        var processed = await replayer.ReplayStartedOffboardingsAsync(companyId, CancellationToken.None);

        Assert.Equal(2, processed);
        Assert.Equal(2, publisher.Published.Count);

        var event1 = Assert.Single(publisher.Published.Cast<OffboardingStartedIntegrationEvent>(),
            e => e.EmployeeId == employeeId1);
        Assert.Equal(companyId, event1.CompanyId);
        Assert.Equal(createdAt1, event1.OccurredAt);

        var event2 = Assert.Single(publisher.Published.Cast<OffboardingStartedIntegrationEvent>(),
            e => e.EmployeeId == employeeId2);
        Assert.Equal(companyId, event2.CompanyId);
        Assert.Equal(createdAt2, event2.OccurredAt);
    }

    [Fact]
    public async Task ReplayStartedOffboardingsAsync_Does_Not_Replay_Plans_From_Other_Companies()
    {
        await using var dbContext = BuildContext();
        var companyId = Guid.NewGuid();
        var otherCompanyId = Guid.NewGuid();

        AddPlan(dbContext, companyId, Guid.NewGuid(), Now.AddDays(-1), p => p.Start(Now.AddDays(-1)));
        AddPlan(dbContext, otherCompanyId, Guid.NewGuid(), Now.AddDays(-1), p => p.Start(Now.AddDays(-1)));
        await dbContext.SaveChangesAsync();

        var publisher = new CapturingIntegrationEventPublisher();
        var replayer = new OffboardingHistoryReplayer(dbContext, publisher);

        var processed = await replayer.ReplayStartedOffboardingsAsync(companyId, CancellationToken.None);

        Assert.Equal(1, processed);
        var evt = Assert.Single(publisher.Published.Cast<OffboardingStartedIntegrationEvent>());
        Assert.Equal(companyId, evt.CompanyId);
    }

    [Fact]
    public async Task ReplayStartedOffboardingsAsync_Returns_Zero_And_Publishes_Nothing_When_No_Plans()
    {
        await using var dbContext = BuildContext();
        var companyId = Guid.NewGuid();

        var publisher = new CapturingIntegrationEventPublisher();
        var replayer = new OffboardingHistoryReplayer(dbContext, publisher);

        var processed = await replayer.ReplayStartedOffboardingsAsync(companyId, CancellationToken.None);

        Assert.Equal(0, processed);
        Assert.Empty(publisher.Published);
    }

    [Fact]
    public async Task ReplayStartedOffboardingsAsync_Has_No_Self_Dedup_And_Republishes_On_Every_Call()
    {
        await using var dbContext = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        AddPlan(dbContext, companyId, employeeId, Now.AddDays(-1), p => p.Start(Now.AddDays(-1)));
        await dbContext.SaveChangesAsync();

        var publisher = new CapturingIntegrationEventPublisher();
        var replayer = new OffboardingHistoryReplayer(dbContext, publisher);

        var firstProcessed = await replayer.ReplayStartedOffboardingsAsync(companyId, CancellationToken.None);
        var secondProcessed = await replayer.ReplayStartedOffboardingsAsync(companyId, CancellationToken.None);

        Assert.Equal(1, firstProcessed);
        Assert.Equal(1, secondProcessed);
        Assert.Equal(2, publisher.Published.Count);
    }
}
