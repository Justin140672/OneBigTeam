using HR.Modules.Leave.Domain;
using HR.Modules.Leave.Features.UpdateLeaveRequestDraft;
using HR.Modules.Leave.Persistence;
using HR.Modules.Leave.Tests.Infrastructure;

using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Leave.Tests;

public class UpdateLeaveRequestDraftHandlerTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 6, 12, 9, 0, 0, DateTimeKind.Utc);

    private static LeaveDbContext BuildContext()
    {
        var options = new DbContextOptionsBuilder<LeaveDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new LeaveDbContext(options);
    }

    private static UpdateLeaveRequestDraftHandler BuildHandler(LeaveDbContext context) =>
        new(context, new FakeClock(FixedUtcNow), new FakeWorkingPatternProvider(),
            new FakeCompanyLeaveSettingsReader(), new FakePublicHolidayReader());

    private static UpdateLeaveRequestDraftRequest UpdateRequest(Guid companyId, Guid employeeId, Guid leaveRequestId, Guid leaveTypeId) => new()
    {
        CompanyId = companyId,
        EmployeeId = employeeId,
        LeaveRequestId = leaveRequestId,
        LeaveTypeId = leaveTypeId,
        StartDate = new DateOnly(2026, 8, 10),
        StartPart = LeaveDayPart.FullDay,
        EndDate = new DateOnly(2026, 8, 11),
        EndPart = LeaveDayPart.FullDay,
        Reason = "Updated reason"
    };

    private static async Task<(LeaveType LeaveType, LeaveRequest Draft)> SeedDraftAsync(
        LeaveDbContext context, Guid companyId, Guid employeeId, DateTimeOffset now)
    {
        var leaveType = LeaveType.Create(Guid.NewGuid(), companyId, "Annual Leave", "ANNUAL", 25,
            AccrualMethod.None, LeaveTypeBehaviour.Standard, now);

        var draft = LeaveRequest.CreateDraft(
            Guid.NewGuid(), companyId, employeeId, leaveType.Id, null,
            new DateOnly(2026, 8, 3), LeaveDayPart.FullDay,
            new DateOnly(2026, 8, 7), LeaveDayPart.FullDay,
            5m, "Original reason", now);

        context.LeaveTypes.Add(leaveType);
        context.LeaveRequests.Add(draft);
        await context.SaveChangesAsync();

        return (leaveType, draft);
    }

    [Fact]
    public async Task HandleAsync_Updates_Draft_Fields()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        var (leaveType, draft) = await SeedDraftAsync(context, companyId, employeeId, now);

        var handler = BuildHandler(context);
        var result = await handler.HandleAsync(
            UpdateRequest(companyId, employeeId, draft.Id, leaveType.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Draft", result.Value!.Status);
        Assert.Equal(new DateOnly(2026, 8, 10), result.Value.StartDate);
        Assert.Equal(new DateOnly(2026, 8, 11), result.Value.EndDate);
        Assert.Equal("Updated reason", result.Value.Reason);

        var saved = await context.LeaveRequests.SingleAsync();
        Assert.Equal(new DateOnly(2026, 8, 10), saved.StartDate);
        Assert.Equal("Updated reason", saved.Reason);
    }

    [Theory]
    [InlineData((int)LeaveRequestStatus.Pending)]
    [InlineData((int)LeaveRequestStatus.Approved)]
    [InlineData((int)LeaveRequestStatus.Rejected)]
    [InlineData((int)LeaveRequestStatus.Cancelled)]
    public async Task HandleAsync_Returns_Validation_Error_When_Request_Is_Not_Draft(int statusValue)
    {
        var status = (LeaveRequestStatus)statusValue;
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        var (leaveType, draft) = await SeedDraftAsync(context, companyId, employeeId, now);

        switch (status)
        {
            case LeaveRequestStatus.Pending:
                draft.MarkSubmittedPending(now);
                break;
            case LeaveRequestStatus.Approved:
                draft.Approve(Guid.NewGuid(), now);
                break;
            case LeaveRequestStatus.Rejected:
                draft.MarkSubmittedPending(now);
                draft.Reject(Guid.NewGuid(), now);
                break;
            case LeaveRequestStatus.Cancelled:
                draft.MarkSubmittedPending(now);
                draft.Cancel(now);
                break;
        }
        await context.SaveChangesAsync();

        var handler = BuildHandler(context);
        var result = await handler.HandleAsync(
            UpdateRequest(companyId, employeeId, draft.Id, leaveType.Id), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("validation", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_Id_Does_Not_Exist()
    {
        await using var context = BuildContext();
        var handler = BuildHandler(context);

        var result = await handler.HandleAsync(
            UpdateRequest(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_Draft_Belongs_To_Different_Employee()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        var (leaveType, draft) = await SeedDraftAsync(context, companyId, employeeId, now);

        var handler = BuildHandler(context);
        var result = await handler.HandleAsync(
            UpdateRequest(companyId, Guid.NewGuid(), draft.Id, leaveType.Id), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_Draft_Belongs_To_Different_Company()
    {
        await using var context = BuildContext();
        var companyA = Guid.NewGuid();
        var companyB = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        var (leaveType, draft) = await SeedDraftAsync(context, companyA, employeeId, now);

        var handler = BuildHandler(context);
        var result = await handler.HandleAsync(
            UpdateRequest(companyB, employeeId, draft.Id, leaveType.Id), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }
}

public class UpdateLeaveRequestDraftValidatorTests
{
    private readonly UpdateLeaveRequestDraftValidator _validator = new();

    private static UpdateLeaveRequestDraftRequest ValidRequest() => new()
    {
        CompanyId = Guid.NewGuid(),
        EmployeeId = Guid.NewGuid(),
        LeaveRequestId = Guid.NewGuid(),
        LeaveTypeId = Guid.NewGuid(),
        StartDate = new DateOnly(2026, 8, 3),
        StartPart = LeaveDayPart.FullDay,
        EndDate = new DateOnly(2026, 8, 7),
        EndPart = LeaveDayPart.FullDay,
        Reason = "Holiday"
    };

    [Fact]
    public void Validate_Succeeds_For_Valid_Request()
    {
        Assert.True(_validator.Validate(ValidRequest()).IsValid);
    }

    [Fact]
    public void Validate_Fails_When_LeaveRequestId_Is_Empty()
    {
        Assert.False(_validator.Validate(ValidRequest() with { LeaveRequestId = Guid.Empty }).IsValid);
    }

    [Fact]
    public void Validate_Fails_When_EndDate_Is_Before_StartDate()
    {
        var result = _validator.Validate(ValidRequest() with
        {
            StartDate = new DateOnly(2026, 8, 7),
            EndDate = new DateOnly(2026, 8, 3)
        });

        Assert.False(result.IsValid);
    }

    [Fact]
    public void Validate_Fails_When_Reason_Exceeds_MaximumLength()
    {
        Assert.False(_validator.Validate(ValidRequest() with { Reason = new string('a', 1001) }).IsValid);
    }

    [Fact]
    public void Validate_Succeeds_When_Reason_Is_Exactly_MaximumLength()
    {
        Assert.True(_validator.Validate(ValidRequest() with { Reason = new string('a', 1000) }).IsValid);
    }
}
