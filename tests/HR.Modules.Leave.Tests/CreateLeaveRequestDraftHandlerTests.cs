using HR.Modules.Leave.Domain;
using HR.Modules.Leave.Features.CreateLeaveRequestDraft;
using HR.Modules.Leave.Persistence;
using HR.Modules.Leave.Tests.Infrastructure;

using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Leave.Tests;

public class CreateLeaveRequestDraftHandlerTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 6, 12, 9, 0, 0, DateTimeKind.Utc);

    private static LeaveDbContext BuildContext()
    {
        var options = new DbContextOptionsBuilder<LeaveDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new LeaveDbContext(options);
    }

    private static CreateLeaveRequestDraftHandler BuildHandler(LeaveDbContext context) =>
        new(context, new FakeClock(FixedUtcNow), new FakeWorkingPatternProvider(),
            new FakeCompanyLeaveSettingsReader(), new FakePublicHolidayReader());

    // 2026-08-03 = Monday, 2026-08-07 = Friday
    private static CreateLeaveRequestDraftRequest ValidRequest(Guid companyId, Guid employeeId, Guid leaveTypeId) => new()
    {
        CompanyId = companyId,
        EmployeeId = employeeId,
        LeaveTypeId = leaveTypeId,
        StartDate = new DateOnly(2026, 8, 3),
        StartPart = LeaveDayPart.FullDay,
        EndDate = new DateOnly(2026, 8, 7),
        EndPart = LeaveDayPart.FullDay,
        Reason = "Family holiday"
    };

    [Fact]
    public async Task HandleAsync_Creates_Draft_With_Draft_Status()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        var leaveType = LeaveType.Create(Guid.NewGuid(), companyId, "Annual Leave", "ANNUAL", 25,
            AccrualMethod.None, LeaveTypeBehaviour.Standard, now);
        context.LeaveTypes.Add(leaveType);
        await context.SaveChangesAsync();

        var handler = BuildHandler(context);
        var result = await handler.HandleAsync(ValidRequest(companyId, employeeId, leaveType.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Draft", result.Value!.Status);
        Assert.Equal(5m, result.Value.TotalDays);

        var saved = await context.LeaveRequests.SingleAsync();
        Assert.Equal(LeaveRequestStatus.Draft, saved.Status);
    }

    [Fact]
    public async Task HandleAsync_Does_Not_Create_LeaveBalance_Or_ToilTransaction_Rows()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        var leaveType = LeaveType.Create(Guid.NewGuid(), companyId, "Annual Leave", "ANNUAL", 25,
            AccrualMethod.None, LeaveTypeBehaviour.Standard, now);
        context.LeaveTypes.Add(leaveType);
        await context.SaveChangesAsync();

        var handler = BuildHandler(context);
        var result = await handler.HandleAsync(ValidRequest(companyId, employeeId, leaveType.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(context.LeaveBalances);
        Assert.Empty(context.ToilTransactions);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_LeaveType_Does_Not_Exist()
    {
        await using var context = BuildContext();
        var handler = BuildHandler(context);

        var result = await handler.HandleAsync(
            ValidRequest(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Succeeds_Without_Employee_Having_Policy_Assignment()
    {
        // Drafts can be created before a policy assignment exists — no cross-year/balance checks.
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        var leaveType = LeaveType.Create(Guid.NewGuid(), companyId, "Annual Leave", "ANNUAL", 0,
            AccrualMethod.None, LeaveTypeBehaviour.Standard, now);
        context.LeaveTypes.Add(leaveType);
        await context.SaveChangesAsync();

        var handler = BuildHandler(context);
        var result = await handler.HandleAsync(ValidRequest(companyId, employeeId, leaveType.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value!.LeavePolicyId);
    }
}

public class CreateLeaveRequestDraftValidatorTests
{
    private readonly CreateLeaveRequestDraftValidator _validator = new();

    private static CreateLeaveRequestDraftRequest ValidRequest() => new()
    {
        CompanyId = Guid.NewGuid(),
        EmployeeId = Guid.NewGuid(),
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
        var result = _validator.Validate(ValidRequest());
        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_Fails_When_CompanyId_Is_Empty()
    {
        var result = _validator.Validate(ValidRequest() with { CompanyId = Guid.Empty });
        Assert.False(result.IsValid);
    }

    [Fact]
    public void Validate_Fails_When_EmployeeId_Is_Empty()
    {
        var result = _validator.Validate(ValidRequest() with { EmployeeId = Guid.Empty });
        Assert.False(result.IsValid);
    }

    [Fact]
    public void Validate_Fails_When_LeaveTypeId_Is_Empty()
    {
        var result = _validator.Validate(ValidRequest() with { LeaveTypeId = Guid.Empty });
        Assert.False(result.IsValid);
    }

    [Fact]
    public void Validate_Fails_When_StartDate_Is_MinValue()
    {
        var result = _validator.Validate(ValidRequest() with { StartDate = DateOnly.MinValue });
        Assert.False(result.IsValid);
    }

    [Fact]
    public void Validate_Fails_When_EndDate_Is_MinValue()
    {
        var result = _validator.Validate(ValidRequest() with { EndDate = DateOnly.MinValue });
        Assert.False(result.IsValid);
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
    public void Validate_Succeeds_When_EndDate_Equals_StartDate()
    {
        var result = _validator.Validate(ValidRequest() with
        {
            StartDate = new DateOnly(2026, 8, 3),
            EndDate = new DateOnly(2026, 8, 3)
        });

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_Fails_When_Reason_Exceeds_MaximumLength()
    {
        var result = _validator.Validate(ValidRequest() with { Reason = new string('a', 1001) });
        Assert.False(result.IsValid);
    }

    [Fact]
    public void Validate_Succeeds_When_Reason_Is_Exactly_MaximumLength()
    {
        var result = _validator.Validate(ValidRequest() with { Reason = new string('a', 1000) });
        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_Succeeds_When_Reason_Is_Null()
    {
        var result = _validator.Validate(ValidRequest() with { Reason = null });
        Assert.True(result.IsValid);
    }
}
