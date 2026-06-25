using HR.Modules.Probation.Domain;
using HR.Modules.Probation.Features.CreateProbationOnEmployeeCreated;
using HR.Modules.Probation.Persistence;
using HR.Modules.Probation.Tests.Infrastructure;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Probation.Tests;

public class EmployeeCreatedHandlerTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 6, 25, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateTimeOffset Now = new(FixedUtcNow, TimeSpan.Zero);

    [Fact]
    public async Task HandleAsync_Creates_ProbationRecord_When_Manager_Is_Present()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var managerId = Guid.NewGuid();
        var startDate = new DateOnly(2026, 7, 1);

        var handler = new EmployeeCreatedHandler(context, new FakeClock(FixedUtcNow));

        await handler.HandleAsync(
            new EmployeeCreatedIntegrationEvent(companyId, employeeId, startDate, managerId),
            CancellationToken.None);

        var record = await context.ProbationRecords.SingleAsync();
        Assert.Equal(companyId, record.CompanyId);
        Assert.Equal(employeeId, record.EmployeeId);
        Assert.Equal(managerId, record.ManagerEmployeeId);
        Assert.Equal(startDate, record.StartDate);
        Assert.Equal(startDate.AddDays(90), record.ExpectedEndDate);
        Assert.Equal(ProbationStatus.Active, record.Status);
        Assert.Null(record.Notes);
        Assert.Equal(Now, record.CreatedAt);
    }

    [Fact]
    public async Task HandleAsync_Sets_ExpectedEndDate_To_90_Days_After_StartDate()
    {
        await using var context = BuildContext();
        var startDate = new DateOnly(2026, 1, 1);

        var handler = new EmployeeCreatedHandler(context, new FakeClock(FixedUtcNow));

        await handler.HandleAsync(
            new EmployeeCreatedIntegrationEvent(Guid.NewGuid(), Guid.NewGuid(), startDate, Guid.NewGuid()),
            CancellationToken.None);

        var record = await context.ProbationRecords.SingleAsync();
        Assert.Equal(new DateOnly(2026, 4, 1), record.ExpectedEndDate);
    }

    [Fact]
    public async Task HandleAsync_Does_Nothing_When_Manager_Is_Null()
    {
        await using var context = BuildContext();

        var handler = new EmployeeCreatedHandler(context, new FakeClock(FixedUtcNow));

        await handler.HandleAsync(
            new EmployeeCreatedIntegrationEvent(Guid.NewGuid(), Guid.NewGuid(), new DateOnly(2026, 7, 1), ManagerId: null),
            CancellationToken.None);

        Assert.Equal(0, await context.ProbationRecords.CountAsync());
    }

    [Fact]
    public async Task HandleAsync_Creates_Separate_Records_For_Different_Employees()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var managerId = Guid.NewGuid();
        var startDate = new DateOnly(2026, 7, 1);

        var handler = new EmployeeCreatedHandler(context, new FakeClock(FixedUtcNow));

        await handler.HandleAsync(
            new EmployeeCreatedIntegrationEvent(companyId, Guid.NewGuid(), startDate, managerId),
            CancellationToken.None);

        await handler.HandleAsync(
            new EmployeeCreatedIntegrationEvent(companyId, Guid.NewGuid(), startDate, managerId),
            CancellationToken.None);

        Assert.Equal(2, await context.ProbationRecords.CountAsync());
    }

    private static ProbationDbContext BuildContext() =>
        new(new DbContextOptionsBuilder<ProbationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);
}
