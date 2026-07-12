using HR.Modules.Offboarding.Domain;
using HR.Modules.Offboarding.Features.GetOffboardingStatus;
using HR.Modules.Offboarding.Persistence;
using HR.Modules.Offboarding.Services;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Offboarding.Tests;

public class GetOffboardingStatusHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 10, 10, 0, 0, TimeSpan.Zero);

    private static OffboardingDbContext BuildContext() =>
        new(new DbContextOptionsBuilder<OffboardingDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);

    private static OffboardingPlan SeedPlan(
        OffboardingDbContext dbContext,
        Guid companyId,
        Guid employeeId,
        DateTimeOffset createdAt,
        OffboardingStatus? status = null)
    {
        var plan = OffboardingPlan.Create(
            Guid.NewGuid(), companyId, employeeId, DateOnly.FromDateTime(createdAt.Date), null, createdAt);

        if (status == OffboardingStatus.InProgress)
            plan.Start(createdAt);
        else if (status == OffboardingStatus.Completed)
            plan.Complete(createdAt);
        else if (status == OffboardingStatus.Cancelled)
            plan.Cancel(null, createdAt);

        dbContext.OffboardingPlans.Add(plan);
        return plan;
    }

    [Fact]
    public async Task HandleAsync_Returns_HasPlan_False_When_No_Plan_Exists()
    {
        await using var dbContext = BuildContext();
        var handler = new GetOffboardingStatusHandler(new OffboardingStatusReader(dbContext));

        var result = await handler.HandleAsync(
            new GetOffboardingStatusRequest { CompanyId = Guid.NewGuid(), EmployeeId = Guid.NewGuid() },
            CancellationToken.None);

        Assert.False(result.HasPlan);
        Assert.Null(result.Status);
    }

    [Theory]
    [InlineData(null, "NotStarted")]
    [InlineData("InProgress", "InProgress")]
    [InlineData("Completed", "Completed")]
    [InlineData("Cancelled", "Cancelled")]
    public async Task HandleAsync_Returns_HasPlan_True_With_Current_Status(string? statusName, string expected)
    {
        await using var dbContext = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        OffboardingStatus? status = statusName switch
        {
            "InProgress" => OffboardingStatus.InProgress,
            "Completed" => OffboardingStatus.Completed,
            "Cancelled" => OffboardingStatus.Cancelled,
            _ => null
        };

        SeedPlan(dbContext, companyId, employeeId, Now, status);
        await dbContext.SaveChangesAsync();

        var handler = new GetOffboardingStatusHandler(new OffboardingStatusReader(dbContext));

        var result = await handler.HandleAsync(
            new GetOffboardingStatusRequest { CompanyId = companyId, EmployeeId = employeeId },
            CancellationToken.None);

        Assert.True(result.HasPlan);
        Assert.Equal(expected, result.Status);
    }

    [Fact]
    public async Task HandleAsync_Returns_Most_Recent_Plan_By_CreatedAt_When_Employee_Has_Multiple()
    {
        await using var dbContext = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        SeedPlan(dbContext, companyId, employeeId, Now.AddYears(-1), OffboardingStatus.Cancelled);
        SeedPlan(dbContext, companyId, employeeId, Now, OffboardingStatus.InProgress);
        await dbContext.SaveChangesAsync();

        var handler = new GetOffboardingStatusHandler(new OffboardingStatusReader(dbContext));

        var result = await handler.HandleAsync(
            new GetOffboardingStatusRequest { CompanyId = companyId, EmployeeId = employeeId },
            CancellationToken.None);

        Assert.True(result.HasPlan);
        Assert.Equal("InProgress", result.Status);
    }

    [Fact]
    public async Task HandleAsync_Is_Company_Scoped()
    {
        await using var dbContext = BuildContext();
        var employeeId = Guid.NewGuid();

        SeedPlan(dbContext, Guid.NewGuid(), employeeId, Now, OffboardingStatus.InProgress);
        await dbContext.SaveChangesAsync();

        var handler = new GetOffboardingStatusHandler(new OffboardingStatusReader(dbContext));

        var result = await handler.HandleAsync(
            new GetOffboardingStatusRequest { CompanyId = Guid.NewGuid(), EmployeeId = employeeId },
            CancellationToken.None);

        Assert.False(result.HasPlan);
    }
}
