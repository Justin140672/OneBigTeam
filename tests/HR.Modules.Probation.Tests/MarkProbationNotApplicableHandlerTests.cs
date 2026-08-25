using HR.Modules.Probation.Domain;
using HR.Modules.Probation.Features.MarkProbationNotApplicable;
using HR.Modules.Probation.Persistence;
using HR.Modules.Probation.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Probation.Tests;

public class MarkProbationNotApplicableHandlerTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 6, 25, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateTimeOffset Now = new(FixedUtcNow, TimeSpan.Zero);
    private static readonly DateOnly StartDate = new(2026, 1, 1);
    private static readonly DateOnly ExpectedEndDate = new(2026, 4, 1);

    [Fact]
    public async Task HandleAsync_Existing_NotStarted_Record_Transitions_To_NotApplicable_And_Publishes_Audit()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var managerId = Guid.NewGuid();
        var futureStart = DateOnly.FromDateTime(FixedUtcNow).AddDays(10);

        var record = ProbationRecord.Create(
            Guid.NewGuid(), companyId, employeeId, managerId, futureStart, futureStart.AddMonths(3), null,
            DateOnly.FromDateTime(FixedUtcNow), Now);
        Assert.Equal(ProbationStatus.NotStarted, record.Status);
        context.ProbationRecords.Add(record);
        await context.SaveChangesAsync();

        var auditPublisher = new FakeAuditPublisher();
        var handler = new MarkProbationNotApplicableHandler(context, new FakeClock(FixedUtcNow), auditPublisher);

        var result = await handler.HandleAsync(
            new MarkProbationNotApplicableRequest
            {
                CompanyId = companyId,
                EmployeeId = employeeId,
                Reason = "Role is exempt."
            },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("NotApplicable", result.Value!.Status);
        Assert.Equal("Role is exempt.", result.Value.NotApplicableReason);

        var reloaded = await context.ProbationRecords.SingleAsync();
        Assert.Equal(ProbationStatus.NotApplicable, reloaded.Status);
        Assert.Equal("Role is exempt.", reloaded.NotApplicableReason);

        var evt = Assert.IsType<ProbationMarkedNotApplicableAuditEvent>(Assert.Single(auditPublisher.Published));
        Assert.True(evt.HasReason);

        var serialized = System.Text.Json.JsonSerializer.Serialize(evt);
        Assert.DoesNotContain("Role is exempt.", serialized);
    }

    [Fact]
    public async Task HandleAsync_Existing_Record_Transitions_Publishes_Audit_With_Actor_And_HasReason_False_When_No_Reason()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var managerId = Guid.NewGuid();
        var actorEmployeeId = Guid.NewGuid();

        var record = ProbationRecord.Create(
            Guid.NewGuid(), companyId, employeeId, managerId, StartDate, ExpectedEndDate, null,
            DateOnly.FromDateTime(FixedUtcNow), Now);
        context.ProbationRecords.Add(record);
        await context.SaveChangesAsync();

        var auditPublisher = new FakeAuditPublisher();
        var handler = new MarkProbationNotApplicableHandler(context, new FakeClock(FixedUtcNow), auditPublisher);

        var result = await handler.HandleAsync(
            new MarkProbationNotApplicableRequest
            {
                CompanyId = companyId,
                EmployeeId = employeeId,
                ActorEmployeeId = actorEmployeeId
            },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        var evt = Assert.IsType<ProbationMarkedNotApplicableAuditEvent>(Assert.Single(auditPublisher.Published));
        Assert.Equal(actorEmployeeId, evt.ActorEmployeeIdValue);
        Assert.False(evt.HasReason);
    }

    [Fact]
    public async Task HandleAsync_Existing_Active_Record_Transitions_To_NotApplicable()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var managerId = Guid.NewGuid();

        var record = ProbationRecord.Create(
            Guid.NewGuid(), companyId, employeeId, managerId, StartDate, ExpectedEndDate, null,
            DateOnly.FromDateTime(FixedUtcNow), Now);
        Assert.Equal(ProbationStatus.Active, record.Status);
        context.ProbationRecords.Add(record);
        await context.SaveChangesAsync();

        var handler = new MarkProbationNotApplicableHandler(context, new FakeClock(FixedUtcNow), new FakeAuditPublisher());

        var result = await handler.HandleAsync(
            new MarkProbationNotApplicableRequest { CompanyId = companyId, EmployeeId = employeeId },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        var reloaded = await context.ProbationRecords.SingleAsync();
        Assert.Equal(ProbationStatus.NotApplicable, reloaded.Status);
    }

    [Theory]
    [InlineData("ReviewDue")]
    [InlineData("Extended")]
    [InlineData("Passed")]
    [InlineData("Failed")]
    [InlineData("NotApplicable")]
    public async Task HandleAsync_Existing_Record_In_NonTransitionable_Status_Returns_Conflict_And_Leaves_Record_Unchanged(
        string status)
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var managerId = Guid.NewGuid();

        var record = ProbationRecord.Create(
            Guid.NewGuid(), companyId, employeeId, managerId, StartDate, ExpectedEndDate, null,
            DateOnly.FromDateTime(FixedUtcNow), Now);

        switch (status)
        {
            case "ReviewDue":
                record.MarkReviewDue(Now);
                break;
            case "Extended":
                record.Extend(ExpectedEndDate.AddMonths(1), "Needs more time.", managerId, ExpectedEndDate.AddDays(-1), Now);
                break;
            case "Passed":
                record.Pass(managerId, ExpectedEndDate, null, Now);
                break;
            case "Failed":
                record.Fail(managerId, ExpectedEndDate, null, Now);
                break;
            case "NotApplicable":
                record.MarkNotApplicable("Original reason.", Now);
                break;
        }

        context.ProbationRecords.Add(record);
        await context.SaveChangesAsync();

        var handler = new MarkProbationNotApplicableHandler(context, new FakeClock(FixedUtcNow), new FakeAuditPublisher());

        var result = await handler.HandleAsync(
            new MarkProbationNotApplicableRequest { CompanyId = companyId, EmployeeId = employeeId },
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("conflict", result.Error.Code);

        var reloaded = await context.ProbationRecords.SingleAsync();
        Assert.Equal(Enum.Parse<ProbationStatus>(status), reloaded.Status);
    }

    [Fact]
    public async Task HandleAsync_No_Existing_Record_With_All_Optional_Fields_Supplied_Creates_NotApplicable_Record()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var managerId = Guid.NewGuid();

        var auditPublisher = new FakeAuditPublisher();
        var handler = new MarkProbationNotApplicableHandler(context, new FakeClock(FixedUtcNow), auditPublisher);
        var actorEmployeeId = Guid.NewGuid();

        var result = await handler.HandleAsync(
            new MarkProbationNotApplicableRequest
            {
                CompanyId = companyId,
                EmployeeId = employeeId,
                ManagerEmployeeId = managerId,
                StartDate = StartDate,
                ExpectedEndDate = ExpectedEndDate,
                Reason = "Exempt employment type.",
                ActorEmployeeId = actorEmployeeId
            },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("NotApplicable", result.Value!.Status);

        var created = await context.ProbationRecords.SingleAsync();
        Assert.Equal(companyId, created.CompanyId);
        Assert.Equal(employeeId, created.EmployeeId);
        Assert.Equal(managerId, created.ManagerEmployeeId);
        Assert.Equal(StartDate, created.StartDate);
        Assert.Equal(ExpectedEndDate, created.ExpectedEndDate);
        Assert.Equal(ProbationStatus.NotApplicable, created.Status);
        Assert.Equal("Exempt employment type.", created.NotApplicableReason);

        var evt = Assert.IsType<ProbationMarkedNotApplicableAuditEvent>(Assert.Single(auditPublisher.Published));
        Assert.Equal(actorEmployeeId, evt.ActorEmployeeIdValue);
        Assert.True(evt.HasReason);

        var serialized = System.Text.Json.JsonSerializer.Serialize(evt);
        Assert.DoesNotContain("Exempt employment type.", serialized);
    }

    [Fact]
    public async Task HandleAsync_No_Existing_Record_With_Missing_Optional_Fields_Returns_Validation_Failure_And_Creates_No_Record()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        var handler = new MarkProbationNotApplicableHandler(context, new FakeClock(FixedUtcNow), new FakeAuditPublisher());

        var result = await handler.HandleAsync(
            new MarkProbationNotApplicableRequest { CompanyId = companyId, EmployeeId = employeeId },
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("validation", result.Error.Code);
        Assert.Equal(0, await context.ProbationRecords.CountAsync());
    }

    private static ProbationDbContext BuildContext() =>
        new(new DbContextOptionsBuilder<ProbationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);
}
