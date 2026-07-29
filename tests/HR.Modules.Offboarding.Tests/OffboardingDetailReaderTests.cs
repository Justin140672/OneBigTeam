using HR.Modules.Offboarding.Domain;
using HR.Modules.Offboarding.Persistence;
using HR.Modules.Offboarding.Services;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Offboarding.Tests;

public class OffboardingDetailReaderTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 17, 10, 0, 0, TimeSpan.Zero);

    private static OffboardingDbContext BuildContext() =>
        new(new DbContextOptionsBuilder<OffboardingDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);

    [Fact]
    public async Task GetDetailAsync_Returns_Null_When_No_Plan_Exists()
    {
        await using var db = BuildContext();
        var reader = new OffboardingDetailReader(db);

        var result = await reader.GetDetailAsync(Guid.NewGuid(), Guid.NewGuid(), CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task GetDetailAsync_Maps_Status_And_LastWorkingDay()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var lastWorkingDay = new DateOnly(2026, 8, 1);

        var plan = OffboardingPlan.Create(Guid.NewGuid(), companyId, employeeId, lastWorkingDay, null, Now);
        plan.Start(Now);
        db.OffboardingPlans.Add(plan);
        await db.SaveChangesAsync();

        var reader = new OffboardingDetailReader(db);

        var result = await reader.GetDetailAsync(companyId, employeeId, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal("InProgress", result!.Status);
        Assert.Equal(lastWorkingDay, result.LastWorkingDay);
    }

    [Fact]
    public async Task GetDetailAsync_Returns_Most_Recently_Created_Plan_When_Employee_Has_Multiple()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        var older = OffboardingPlan.Create(
            Guid.NewGuid(), companyId, employeeId, new DateOnly(2026, 1, 1), null, Now.AddMonths(-6));
        older.Cancel("changed mind", Now.AddMonths(-6));
        var newer = OffboardingPlan.Create(
            Guid.NewGuid(), companyId, employeeId, new DateOnly(2026, 8, 1), null, Now);

        db.OffboardingPlans.AddRange(older, newer);
        await db.SaveChangesAsync();

        var reader = new OffboardingDetailReader(db);

        var result = await reader.GetDetailAsync(companyId, employeeId, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(new DateOnly(2026, 8, 1), result!.LastWorkingDay);
        Assert.Equal("NotStarted", result.Status);
    }

    [Fact]
    public async Task GetDetailAsync_Is_Scoped_By_CompanyId()
    {
        await using var db = BuildContext();
        var employeeId = Guid.NewGuid();

        var plan = OffboardingPlan.Create(
            Guid.NewGuid(), Guid.NewGuid(), employeeId, new DateOnly(2026, 8, 1), null, Now);
        db.OffboardingPlans.Add(plan);
        await db.SaveChangesAsync();

        var reader = new OffboardingDetailReader(db);

        var result = await reader.GetDetailAsync(Guid.NewGuid(), employeeId, CancellationToken.None);

        Assert.Null(result);
    }
}
