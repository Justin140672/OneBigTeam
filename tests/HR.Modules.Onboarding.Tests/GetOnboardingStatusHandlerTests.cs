using HR.Modules.Onboarding.Domain;
using HR.Modules.Onboarding.Features.GetOnboardingStatus;
using HR.Modules.Onboarding.Persistence;
using HR.Modules.Onboarding.Services;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Onboarding.Tests;

public class GetOnboardingStatusHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 10, 10, 0, 0, TimeSpan.Zero);

    private static OnboardingDbContext BuildContext() =>
        new(new DbContextOptionsBuilder<OnboardingDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);

    private static OnboardingPlan SeedPlan(
        OnboardingDbContext dbContext,
        Guid companyId,
        Guid employeeId,
        DateTimeOffset createdAt,
        OnboardingStatus? status = null)
    {
        var plan = OnboardingPlan.Create(
            Guid.NewGuid(), companyId, employeeId, DateOnly.FromDateTime(createdAt.Date), null, createdAt);

        if (status == OnboardingStatus.InProgress)
            plan.Start(createdAt);
        else if (status == OnboardingStatus.Completed)
            plan.Complete(createdAt);

        dbContext.OnboardingPlans.Add(plan);
        return plan;
    }

    [Fact]
    public async Task HandleAsync_Returns_HasPlan_False_When_No_Plan_Exists()
    {
        await using var dbContext = BuildContext();
        var handler = new GetOnboardingStatusHandler(new OnboardingStatusReader(dbContext));

        var result = await handler.HandleAsync(
            new GetOnboardingStatusRequest { CompanyId = Guid.NewGuid(), EmployeeId = Guid.NewGuid() },
            CancellationToken.None);

        Assert.False(result.HasPlan);
        Assert.Null(result.Status);
    }

    [Theory]
    [InlineData(null, "NotStarted")]
    [InlineData("InProgress", "InProgress")]
    [InlineData("Completed", "Completed")]
    public async Task HandleAsync_Returns_HasPlan_True_With_Current_Status(string? statusName, string expected)
    {
        await using var dbContext = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        OnboardingStatus? status = statusName switch
        {
            "InProgress" => OnboardingStatus.InProgress,
            "Completed" => OnboardingStatus.Completed,
            _ => null
        };

        SeedPlan(dbContext, companyId, employeeId, Now, status);
        await dbContext.SaveChangesAsync();

        var handler = new GetOnboardingStatusHandler(new OnboardingStatusReader(dbContext));

        var result = await handler.HandleAsync(
            new GetOnboardingStatusRequest { CompanyId = companyId, EmployeeId = employeeId },
            CancellationToken.None);

        Assert.True(result.HasPlan);
        Assert.Equal(expected, result.Status);
    }

    [Fact]
    public async Task HandleAsync_Is_Company_Scoped()
    {
        await using var dbContext = BuildContext();
        var employeeId = Guid.NewGuid();

        SeedPlan(dbContext, Guid.NewGuid(), employeeId, Now, OnboardingStatus.InProgress);
        await dbContext.SaveChangesAsync();

        var handler = new GetOnboardingStatusHandler(new OnboardingStatusReader(dbContext));

        var result = await handler.HandleAsync(
            new GetOnboardingStatusRequest { CompanyId = Guid.NewGuid(), EmployeeId = employeeId },
            CancellationToken.None);

        Assert.False(result.HasPlan);
    }
}
