using HR.Modules.Leave.Domain;
using HR.Modules.Leave.Features.PreviewLeaveRequest;
using HR.Modules.Leave.Persistence;
using HR.Modules.Leave.Tests.Infrastructure;
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
        FakeWorkingPatternProvider? workingPattern = null) =>
        new(context,
            new FakeClock(FixedUtcNow),
            workingPattern ?? new FakeWorkingPatternProvider(),
            settings ?? new FakeCompanyLeaveSettingsReader());

    // 2026-08-03 = Monday, 2026-08-07 = Friday
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
        // Wednesday 2026-08-05 is a public holiday
        context.LeaveTypes.Add(leaveType);
        context.PublicHolidays.Add(
            PublicHoliday.Create(Guid.NewGuid(), companyId, new DateOnly(2026, 8, 5), "Summer Bank Holiday", "GB", Now));
        await context.SaveChangesAsync();

        var settings = new FakeCompanyLeaveSettingsReader(
            CompanyLeaveSettings.Default with { ExcludePublicHolidaysFromLeave = true });

        var result = await BuildHandler(context, settings).HandleAsync(
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
        context.PublicHolidays.Add(
            PublicHoliday.Create(Guid.NewGuid(), companyId, new DateOnly(2026, 8, 5), "Summer Bank Holiday", "GB", Now));
        await context.SaveChangesAsync();

        var settings = new FakeCompanyLeaveSettingsReader(
            CompanyLeaveSettings.Default with { ExcludePublicHolidaysFromLeave = false });

        var result = await BuildHandler(context, settings).HandleAsync(
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
        // 2026-08-08 = Saturday — not a working day in default Mon–Fri pattern
        context.PublicHolidays.Add(
            PublicHoliday.Create(Guid.NewGuid(), companyId, new DateOnly(2026, 8, 8), "Weekend Holiday", "GB", Now));
        await context.SaveChangesAsync();

        var settings = new FakeCompanyLeaveSettingsReader(
            CompanyLeaveSettings.Default with { ExcludePublicHolidaysFromLeave = true });

        // Request spans Mon–Mon (2026-08-03 to 2026-08-10) = 6 working days; Sat holiday has no effect
        var result = await BuildHandler(context, settings).HandleAsync(
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
            AccrualMethod.Monthly, LeaveTypeBehaviour.Standard, Now);
        var balance = LeaveBalance.Create(Guid.NewGuid(), companyId, employeeId, leaveType.Id, Guid.NewGuid(),
            2026, 25m, Now);
        context.LeaveTypes.Add(leaveType);
        context.LeaveBalances.Add(balance);
        await context.SaveChangesAsync();

        var settings = new FakeCompanyLeaveSettingsReader(
            CompanyLeaveSettings.Default with { ExcludePublicHolidaysFromLeave = false });

        var result = await BuildHandler(context, settings).HandleAsync(
            BaseRequest(companyId, employeeId, leaveType.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(25m, result.Value!.RemainingBalance);
        Assert.False(result.Value.WouldExceedBalance); // 5 days requested, 25 remaining
    }

    [Fact]
    public async Task HandleAsync_Returns_WouldExceedBalance_True_When_Insufficient_Balance()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        var leaveType = LeaveType.Create(Guid.NewGuid(), companyId, "Annual Leave", "ANNUAL", 25,
            AccrualMethod.Monthly, LeaveTypeBehaviour.Standard, Now);
        var balance = LeaveBalance.Create(Guid.NewGuid(), companyId, employeeId, leaveType.Id, Guid.NewGuid(),
            2026, 3m, Now); // only 3 days, requesting Mon–Fri = 5
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
}
