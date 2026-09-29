using HR.Modules.Leave.Domain;
using HR.Modules.Leave.Features.PreviewLeaveRequest;
using HR.Modules.Leave.Persistence;
using HR.Modules.Leave.Services;
using HR.Modules.Leave.Tests.Infrastructure;
using HR.Infrastructure.Abstractions;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Leave.Tests;

public class PreviewLeaveRequestHandlerTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 6, 12, 9, 0, 0, DateTimeKind.Utc);
    private static readonly DateTimeOffset Now = new(FixedUtcNow, TimeSpan.Zero);

    private static LeaveDbContext BuildContext()
    {
        var options = new DbContextOptionsBuilder<LeaveDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new LeaveDbContext(options);
    }

    private static PreviewLeaveRequestHandler BuildHandler(
        LeaveDbContext context,
        FakeCompanyLeaveSettingsReader? settings = null,
        FakeWorkingPatternProvider? workingPattern = null,
        FakePublicHolidayReader? publicHolidayReader = null) =>
        new(context,
            new FakeClock(FixedUtcNow),
            workingPattern ?? new FakeWorkingPatternProvider(),
            settings ?? new FakeCompanyLeaveSettingsReader(),
            publicHolidayReader ?? new FakePublicHolidayReader(),
            new LeaveWarningCalculator(publicHolidayReader ?? new FakePublicHolidayReader()));

    private static PreviewLeaveRequestRequest BaseRequest(Guid companyId, Guid employeeId, Guid leaveTypeId) => new()
    {
        CompanyId = companyId,
        EmployeeId = employeeId,
        LeaveTypeId = leaveTypeId,
        StartDate = new DateOnly(2026, 8, 3),
        StartPart = LeaveDayPart.FullDay,
        EndDate = new DateOnly(2026, 8, 7),
        EndPart = LeaveDayPart.FullDay
    };

    [Fact]
    public async Task HandleAsync_Returns_TotalDays_And_Empty_Conflicts_When_No_Existing_Requests()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        var leaveType = LeaveType.Create(Guid.NewGuid(), companyId, "Annual Leave", "ANNUAL", 25,
            AccrualMethod.Monthly, LeaveTypeBehaviour.Standard, Now);
        context.LeaveTypes.Add(leaveType);
        await context.SaveChangesAsync();

        var result = await BuildHandler(context).HandleAsync(
            BaseRequest(companyId, employeeId, leaveType.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(5m, result.Value!.TotalDays);
        Assert.Empty(result.Value.Conflicts);
        Assert.Empty(result.Value.ExcludedPublicHolidays);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_LeaveType_Does_Not_Exist()
    {
        await using var context = BuildContext();

        var result = await BuildHandler(context).HandleAsync(
            BaseRequest(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_LeaveType_Is_Inactive()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();

        var leaveType = LeaveType.Create(Guid.NewGuid(), companyId, "Annual Leave", "ANNUAL", 25,
            AccrualMethod.Monthly, LeaveTypeBehaviour.Standard, Now);
        leaveType.Deactivate(Now);
        context.LeaveTypes.Add(leaveType);
        await context.SaveChangesAsync();

        var result = await BuildHandler(context).HandleAsync(
            BaseRequest(companyId, Guid.NewGuid(), leaveType.Id), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Excludes_Public_Holiday_And_Returns_Its_Name_When_Exclusion_Enabled()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        var leaveType = LeaveType.Create(Guid.NewGuid(), companyId, "Annual Leave", "ANNUAL", 25,
            AccrualMethod.Monthly, LeaveTypeBehaviour.Standard, Now);
        context.LeaveTypes.Add(leaveType);
        await context.SaveChangesAsync();

        var settings = new FakeCompanyLeaveSettingsReader(
            CompanyLeaveSettings.Default with { ExcludePublicHolidaysFromLeave = true });
        var reader = new FakePublicHolidayReader([new DateOnly(2026, 8, 5)], "Summer Bank Holiday");

        var result = await BuildHandler(context, settings, publicHolidayReader: reader).HandleAsync(
            BaseRequest(companyId, employeeId, leaveType.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(4m, result.Value!.TotalDays);
        var excluded = Assert.Single(result.Value.ExcludedPublicHolidays);
        Assert.Equal(new DateOnly(2026, 8, 5), excluded.Date);
        Assert.Equal("Summer Bank Holiday", excluded.Name);
    }

    [Fact]
    public async Task HandleAsync_Does_Not_Exclude_Public_Holiday_When_Exclusion_Disabled()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        var leaveType = LeaveType.Create(Guid.NewGuid(), companyId, "Annual Leave", "ANNUAL", 25,
            AccrualMethod.Monthly, LeaveTypeBehaviour.Standard, Now);
        context.LeaveTypes.Add(leaveType);
        await context.SaveChangesAsync();

        var settings = new FakeCompanyLeaveSettingsReader(
            CompanyLeaveSettings.Default with { ExcludePublicHolidaysFromLeave = false });

        var result = await BuildHandler(context, settings, publicHolidayReader: new FakePublicHolidayReader([new DateOnly(2026, 8, 5)])).HandleAsync(
            BaseRequest(companyId, employeeId, leaveType.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(5m, result.Value!.TotalDays);
        Assert.Empty(result.Value.ExcludedPublicHolidays);
    }

    [Fact]
    public async Task HandleAsync_Does_Not_Include_Holiday_On_Non_Working_Day_In_Excluded_List()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        var leaveType = LeaveType.Create(Guid.NewGuid(), companyId, "Annual Leave", "ANNUAL", 25,
            AccrualMethod.Monthly, LeaveTypeBehaviour.Standard, Now);
        context.LeaveTypes.Add(leaveType);
        await context.SaveChangesAsync();

        var settings = new FakeCompanyLeaveSettingsReader(
            CompanyLeaveSettings.Default with { ExcludePublicHolidaysFromLeave = true });
        var reader = new FakePublicHolidayReader([new DateOnly(2026, 8, 8)], "Weekend Holiday");

        var result = await BuildHandler(context, settings, publicHolidayReader: reader).HandleAsync(
            BaseRequest(companyId, employeeId, leaveType.Id) with
            {
                EndDate = new DateOnly(2026, 8, 10)
            },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(6m, result.Value!.TotalDays);
        Assert.Empty(result.Value.ExcludedPublicHolidays);
    }

    [Fact]
    public async Task HandleAsync_Returns_Conflicts_For_Overlapping_Pending_And_Approved_Requests()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        var leaveType = LeaveType.Create(Guid.NewGuid(), companyId, "Annual Leave", "ANNUAL", 25,
            AccrualMethod.Monthly, LeaveTypeBehaviour.Standard, Now);
        context.LeaveTypes.Add(leaveType);

        var pending = LeaveRequest.Create(Guid.NewGuid(), companyId, employeeId, leaveType.Id, Guid.NewGuid(),
            new DateOnly(2026, 8, 5), LeaveDayPart.FullDay, new DateOnly(2026, 8, 6), LeaveDayPart.FullDay,
            2m, "Existing", Now);
        var approved = LeaveRequest.Create(Guid.NewGuid(), companyId, employeeId, leaveType.Id, Guid.NewGuid(),
            new DateOnly(2026, 8, 4), LeaveDayPart.FullDay, new DateOnly(2026, 8, 4), LeaveDayPart.FullDay,
            1m, "Approved", Now);
        approved.Approve(Guid.NewGuid(), Now);

        context.LeaveRequests.AddRange(pending, approved);
        await context.SaveChangesAsync();

        var settings = new FakeCompanyLeaveSettingsReader(
            CompanyLeaveSettings.Default with { ExcludePublicHolidaysFromLeave = false });

        var result = await BuildHandler(context, settings).HandleAsync(
            BaseRequest(companyId, employeeId, leaveType.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value!.Conflicts.Count);
        Assert.Contains(result.Value.Conflicts, c => c.LeaveRequestId == pending.Id && c.Status == "Pending");
        Assert.Contains(result.Value.Conflicts, c => c.LeaveRequestId == approved.Id && c.Status == "Approved");
    }

    [Fact]
    public async Task HandleAsync_Does_Not_Include_Cancelled_Or_Rejected_Requests_As_Conflicts()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        var leaveType = LeaveType.Create(Guid.NewGuid(), companyId, "Annual Leave", "ANNUAL", 25,
            AccrualMethod.Monthly, LeaveTypeBehaviour.Standard, Now);
        context.LeaveTypes.Add(leaveType);

        var cancelled = LeaveRequest.Create(Guid.NewGuid(), companyId, employeeId, leaveType.Id, Guid.NewGuid(),
            new DateOnly(2026, 8, 5), LeaveDayPart.FullDay, new DateOnly(2026, 8, 5), LeaveDayPart.FullDay,
            1m, "Old", Now);
        cancelled.Cancel(Now);

        var rejected = LeaveRequest.Create(Guid.NewGuid(), companyId, employeeId, leaveType.Id, Guid.NewGuid(),
            new DateOnly(2026, 8, 5), LeaveDayPart.FullDay, new DateOnly(2026, 8, 5), LeaveDayPart.FullDay,
            1m, "Old", Now);
        rejected.Reject(Guid.NewGuid(), Now);

        context.LeaveRequests.AddRange(cancelled, rejected);
        await context.SaveChangesAsync();

        var settings = new FakeCompanyLeaveSettingsReader(
            CompanyLeaveSettings.Default with { ExcludePublicHolidaysFromLeave = false });

        var result = await BuildHandler(context, settings).HandleAsync(
            BaseRequest(companyId, employeeId, leaveType.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value!.Conflicts);
    }

    [Fact]
    public async Task HandleAsync_Returns_RemainingBalance_And_WouldExceedBalance_False_When_Sufficient()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        var leaveType = LeaveType.Create(Guid.NewGuid(), companyId, "Annual Leave", "ANNUAL", 25,
            AccrualMethod.None, LeaveTypeBehaviour.Standard, Now);
        var balance = LeaveBalance.Create(Guid.NewGuid(), companyId, employeeId, leaveType.Id, Guid.NewGuid(),
            2026, 25m, new DateOnly(2026, 1, 1), Now);
        context.LeaveTypes.Add(leaveType);
        context.LeaveBalances.Add(balance);
        await context.SaveChangesAsync();

        var settings = new FakeCompanyLeaveSettingsReader(
            CompanyLeaveSettings.Default with { ExcludePublicHolidaysFromLeave = false });

        var result = await BuildHandler(context, settings).HandleAsync(
            BaseRequest(companyId, employeeId, leaveType.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(25m, result.Value!.RemainingBalance);
        Assert.False(result.Value.WouldExceedBalance);
    }

    [Fact]
    public async Task HandleAsync_Returns_WouldExceedBalance_True_When_Insufficient_Balance()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        var leaveType = LeaveType.Create(Guid.NewGuid(), companyId, "Annual Leave", "ANNUAL", 25,
            AccrualMethod.None, LeaveTypeBehaviour.Standard, Now);
        var balance = LeaveBalance.Create(Guid.NewGuid(), companyId, employeeId, leaveType.Id, Guid.NewGuid(),
            2026, 3m, new DateOnly(2026, 1, 1), Now);
        context.LeaveTypes.Add(leaveType);
        context.LeaveBalances.Add(balance);
        await context.SaveChangesAsync();

        var settings = new FakeCompanyLeaveSettingsReader(
            CompanyLeaveSettings.Default with { ExcludePublicHolidaysFromLeave = false });

        var result = await BuildHandler(context, settings).HandleAsync(
            BaseRequest(companyId, employeeId, leaveType.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(3m, result.Value!.RemainingBalance);
        Assert.True(result.Value.WouldExceedBalance);
    }

    [Fact]
    public async Task HandleAsync_Returns_Null_Balance_And_WouldExceedBalance_True_When_No_Balance_Record()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        var leaveType = LeaveType.Create(Guid.NewGuid(), companyId, "Annual Leave", "ANNUAL", 25,
            AccrualMethod.Monthly, LeaveTypeBehaviour.Standard, Now);
        context.LeaveTypes.Add(leaveType);
        await context.SaveChangesAsync();

        var settings = new FakeCompanyLeaveSettingsReader(
            CompanyLeaveSettings.Default with { ExcludePublicHolidaysFromLeave = false });

        var result = await BuildHandler(context, settings).HandleAsync(
            BaseRequest(companyId, employeeId, leaveType.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value!.RemainingBalance);
        Assert.True(result.Value.WouldExceedBalance);
    }

    [Fact]
    public async Task HandleAsync_Reports_Balance_For_Requests_StartDate_Policy_Year_Not_Todays()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        var leaveType = LeaveType.Create(Guid.NewGuid(), companyId, "Annual Leave", "ANNUAL", 25,
            AccrualMethod.None, LeaveTypeBehaviour.Standard, Now);
        var balance2027 = LeaveBalance.Create(Guid.NewGuid(), companyId, employeeId, leaveType.Id, Guid.NewGuid(),
            2027, 25m, new DateOnly(2026, 1, 1), Now);

        context.LeaveTypes.Add(leaveType);
        context.LeaveBalances.Add(balance2027);
        await context.SaveChangesAsync();

        var result = await BuildHandler(context).HandleAsync(
            BaseRequest(companyId, employeeId, leaveType.Id) with
            {
                StartDate = new DateOnly(2027, 1, 4),
                EndDate = new DateOnly(2027, 1, 8)
            },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(25m, result.Value!.RemainingBalance);
        Assert.False(result.Value.WouldExceedBalance);
    }

    [Fact]
    public async Task HandleAsync_Returns_Null_Balance_And_WouldNotExceedBalance_When_LeaveType_HasBalance_Is_False()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        var leaveType = LeaveType.Create(Guid.NewGuid(), companyId, "Unpaid Leave", "UNPAID", 0,
            AccrualMethod.None, LeaveTypeBehaviour.Standard, Now, hasBalance: false);

        var staleBalance = LeaveBalance.Create(Guid.NewGuid(), companyId, employeeId, leaveType.Id, Guid.NewGuid(),
            2026, 0m, new DateOnly(2026, 1, 1), Now);

        context.LeaveTypes.Add(leaveType);
        context.LeaveBalances.Add(staleBalance);
        await context.SaveChangesAsync();

        var result = await BuildHandler(context).HandleAsync(
            BaseRequest(companyId, employeeId, leaveType.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value!.RemainingBalance);
        Assert.False(result.Value.WouldExceedBalance);
    }

    [Fact]
    public async Task HandleAsync_Returns_Validation_Error_When_Request_Spans_Two_Policy_Years()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        var leaveType = LeaveType.Create(Guid.NewGuid(), companyId, "Annual Leave", "ANNUAL", 25,
            AccrualMethod.Monthly, LeaveTypeBehaviour.Standard, Now);
        context.LeaveTypes.Add(leaveType);
        await context.SaveChangesAsync();

        var result = await BuildHandler(context).HandleAsync(
            BaseRequest(companyId, employeeId, leaveType.Id) with
            {
                StartDate = new DateOnly(2026, 12, 28),
                EndDate = new DateOnly(2027, 1, 4)
            },
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("validation", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Does_Not_Reject_Cross_Year_Request_For_Toil_Leave_Type()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        var leaveType = LeaveType.Create(Guid.NewGuid(), companyId, "TOIL", "TOIL", 0,
            AccrualMethod.None, LeaveTypeBehaviour.Toil, Now);
        context.LeaveTypes.Add(leaveType);
        await context.SaveChangesAsync();

        var result = await BuildHandler(context).HandleAsync(
            BaseRequest(companyId, employeeId, leaveType.Id) with
            {
                StartDate = new DateOnly(2026, 12, 28),
                EndDate = new DateOnly(2027, 1, 4)
            },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task HandleAsync_Reports_Accrued_Not_Raw_RemainingBalance_And_WouldExceedBalance_For_Monthly_Accrual()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        var leaveType = LeaveType.Create(Guid.NewGuid(), companyId, "Annual Leave", "ANNUAL", 24,
            AccrualMethod.Monthly, LeaveTypeBehaviour.Standard, Now);
        var balance = LeaveBalance.Create(Guid.NewGuid(), companyId, employeeId, leaveType.Id, Guid.NewGuid(),
            2026, 24m, new DateOnly(2026, 2, 1), Now);
        context.LeaveTypes.Add(leaveType);
        context.LeaveBalances.Add(balance);
        await context.SaveChangesAsync();

        var settings = new FakeCompanyLeaveSettingsReader(
            CompanyLeaveSettings.Default with { ExcludePublicHolidaysFromLeave = false });

        var result = await BuildHandler(context, settings).HandleAsync(
            BaseRequest(companyId, employeeId, leaveType.Id) with { EndDate = new DateOnly(2026, 8, 14) },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(9.5m, result.Value!.RemainingBalance);
        Assert.True(result.Value.WouldExceedBalance);
    }
}
