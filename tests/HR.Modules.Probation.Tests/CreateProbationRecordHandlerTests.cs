using HR.Modules.Probation.Domain;
using HR.Modules.Probation.Features.CreateProbationRecord;
using HR.Modules.Probation.Persistence;
using HR.Modules.Probation.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Probation.Tests;

public class CreateProbationRecordHandlerTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 6, 25, 10, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task HandleAsync_Creates_ProbationRecord_And_Returns_Response()
    {
        await using var context = BuildContext();
        var handler = new CreateProbationRecordHandler(context, new FakeClock(FixedUtcNow), new FakeAuditPublisher());
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var managerId = Guid.NewGuid();

        var result = await handler.HandleAsync(
            new CreateProbationRecordRequest
            {
                CompanyId = companyId,
                EmployeeId = employeeId,
                ManagerEmployeeId = managerId,
                StartDate = new DateOnly(2026, 6, 25),
                ExpectedEndDate = new DateOnly(2026, 9, 25),
                Notes = "New hire probation."
            },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        Assert.Equal(companyId, result.Value!.CompanyId);
        Assert.Equal(employeeId, result.Value.EmployeeId);
        Assert.Equal(managerId, result.Value.ManagerEmployeeId);
        Assert.Equal("Active", result.Value.Status);
        Assert.Equal("New hire probation.", result.Value.Notes);
        Assert.Equal(new DateTimeOffset(FixedUtcNow, TimeSpan.Zero), result.Value.CreatedAt);

        var saved = await context.ProbationRecords.SingleAsync();
        Assert.Equal(result.Value.Id, saved.Id);
    }

    [Fact]
    public async Task HandleAsync_Returns_Conflict_When_Active_Record_Exists()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        context.ProbationRecords.Add(ProbationRecord.Create(
            Guid.NewGuid(), companyId, employeeId, Guid.NewGuid(),
            new DateOnly(2026, 1, 1), new DateOnly(2026, 4, 1), null, now));
        await context.SaveChangesAsync();

        var handler = new CreateProbationRecordHandler(context, new FakeClock(FixedUtcNow), new FakeAuditPublisher());

        var result = await handler.HandleAsync(
            new CreateProbationRecordRequest
            {
                CompanyId = companyId,
                EmployeeId = employeeId,
                ManagerEmployeeId = Guid.NewGuid(),
                StartDate = new DateOnly(2026, 6, 25),
                ExpectedEndDate = new DateOnly(2026, 9, 25)
            },
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("conflict", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_Conflict_When_ReviewDue_Record_Exists()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        var existing = ProbationRecord.Create(
            Guid.NewGuid(), companyId, employeeId, Guid.NewGuid(),
            new DateOnly(2026, 1, 1), new DateOnly(2026, 4, 1), null, now);
        existing.MarkReviewDue(now);
        context.ProbationRecords.Add(existing);
        await context.SaveChangesAsync();

        var handler = new CreateProbationRecordHandler(context, new FakeClock(FixedUtcNow), new FakeAuditPublisher());

        var result = await handler.HandleAsync(
            new CreateProbationRecordRequest
            {
                CompanyId = companyId,
                EmployeeId = employeeId,
                ManagerEmployeeId = Guid.NewGuid(),
                StartDate = new DateOnly(2026, 6, 25),
                ExpectedEndDate = new DateOnly(2026, 9, 25)
            },
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("conflict", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Allows_New_Record_When_Previous_Is_Passed()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var managerId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        var existing = ProbationRecord.Create(
            Guid.NewGuid(), companyId, employeeId, managerId,
            new DateOnly(2025, 1, 1), new DateOnly(2025, 4, 1), null, now);
        existing.Pass(managerId, new DateOnly(2025, 3, 25), null, now);
        context.ProbationRecords.Add(existing);
        await context.SaveChangesAsync();

        var handler = new CreateProbationRecordHandler(context, new FakeClock(FixedUtcNow), new FakeAuditPublisher());

        var result = await handler.HandleAsync(
            new CreateProbationRecordRequest
            {
                CompanyId = companyId,
                EmployeeId = employeeId,
                ManagerEmployeeId = managerId,
                StartDate = new DateOnly(2026, 6, 25),
                ExpectedEndDate = new DateOnly(2026, 9, 25)
            },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task HandleAsync_Allows_Same_Employee_In_Different_Company()
    {
        await using var context = BuildContext();
        var companyA = Guid.NewGuid();
        var companyB = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        context.ProbationRecords.Add(ProbationRecord.Create(
            Guid.NewGuid(), companyA, employeeId, Guid.NewGuid(),
            new DateOnly(2026, 1, 1), new DateOnly(2026, 4, 1), null, now));
        await context.SaveChangesAsync();

        var handler = new CreateProbationRecordHandler(context, new FakeClock(FixedUtcNow), new FakeAuditPublisher());

        var result = await handler.HandleAsync(
            new CreateProbationRecordRequest
            {
                CompanyId = companyB,
                EmployeeId = employeeId,
                ManagerEmployeeId = Guid.NewGuid(),
                StartDate = new DateOnly(2026, 6, 25),
                ExpectedEndDate = new DateOnly(2026, 9, 25)
            },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task HandleAsync_Trims_Notes()
    {
        await using var context = BuildContext();
        var handler = new CreateProbationRecordHandler(context, new FakeClock(FixedUtcNow), new FakeAuditPublisher());

        var result = await handler.HandleAsync(
            new CreateProbationRecordRequest
            {
                CompanyId = Guid.NewGuid(),
                EmployeeId = Guid.NewGuid(),
                ManagerEmployeeId = Guid.NewGuid(),
                StartDate = new DateOnly(2026, 6, 25),
                ExpectedEndDate = new DateOnly(2026, 9, 25),
                Notes = "  Trimmed note.  "
            },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Trimmed note.", result.Value!.Notes);
    }

    private static ProbationDbContext BuildContext() =>
        new(new DbContextOptionsBuilder<ProbationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);
}
