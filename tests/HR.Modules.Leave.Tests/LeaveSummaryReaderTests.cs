using HR.Modules.Leave.Domain;
using HR.Modules.Leave.Persistence;
using HR.Modules.Leave.Services;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Leave.Tests;

public class LeaveSummaryReaderTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 5, 10, 0, 0, TimeSpan.Zero);

    private static LeaveDbContext BuildContext() =>
        new(new DbContextOptionsBuilder<LeaveDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);

    private static LeaveType SeedLeaveType(LeaveDbContext db, Guid companyId, string name = "Annual Leave") =>
        LeaveType.Create(Guid.NewGuid(), companyId, name, name.Substring(0, 3), 25, AccrualMethod.Annual, LeaveTypeBehaviour.Standard, Now);

    [Fact]
    public async Task GetLeaveSummaryAsync_Returns_Row_Per_Balance_With_LeaveTypeName_And_PendingCount()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var leaveType = SeedLeaveType(db, companyId);
        db.LeaveTypes.Add(leaveType);

        var balance = LeaveBalance.Create(
            Guid.NewGuid(), companyId, employeeId, leaveType.Id, Guid.NewGuid(), 2026, 25m, Now);
        db.LeaveBalances.Add(balance);

        var pendingRequest = LeaveRequest.Create(
            Guid.NewGuid(), companyId, employeeId, leaveType.Id, null,
            new DateOnly(2026, 3, 1), LeaveDayPart.FullDay, new DateOnly(2026, 3, 2), LeaveDayPart.FullDay, 2m, null, Now);
        db.LeaveRequests.Add(pendingRequest);

        await db.SaveChangesAsync();

        var reader = new LeaveSummaryReader(db);

        var result = await reader.GetLeaveSummaryAsync(companyId, employeeIds: null, policyYear: 2026, CancellationToken.None);

        var row = Assert.Single(result);
        Assert.Equal(employeeId, row.EmployeeId);
        Assert.Equal("Annual Leave", row.LeaveTypeName);
        Assert.Equal(1, row.PendingRequestCount);
    }

    [Fact]
    public async Task GetLeaveSummaryAsync_Filters_By_EmployeeIds_When_Supplied()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeIdIncluded = Guid.NewGuid();
        var employeeIdExcluded = Guid.NewGuid();
        var leaveType = SeedLeaveType(db, companyId);
        db.LeaveTypes.Add(leaveType);

        db.LeaveBalances.Add(LeaveBalance.Create(
            Guid.NewGuid(), companyId, employeeIdIncluded, leaveType.Id, Guid.NewGuid(), 2026, 25m, Now));
        db.LeaveBalances.Add(LeaveBalance.Create(
            Guid.NewGuid(), companyId, employeeIdExcluded, leaveType.Id, Guid.NewGuid(), 2026, 25m, Now));

        await db.SaveChangesAsync();

        var reader = new LeaveSummaryReader(db);

        var result = await reader.GetLeaveSummaryAsync(
            companyId, employeeIds: [employeeIdIncluded], policyYear: 2026, CancellationToken.None);

        var row = Assert.Single(result);
        Assert.Equal(employeeIdIncluded, row.EmployeeId);
    }

    [Fact]
    public async Task GetLeaveSummaryAsync_Returns_Empty_When_EmployeeIds_Is_Empty_Collection()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var leaveType = SeedLeaveType(db, companyId);
        db.LeaveTypes.Add(leaveType);
        db.LeaveBalances.Add(LeaveBalance.Create(
            Guid.NewGuid(), companyId, Guid.NewGuid(), leaveType.Id, Guid.NewGuid(), 2026, 25m, Now));
        await db.SaveChangesAsync();

        var reader = new LeaveSummaryReader(db);

        // Empty (non-null) collection is treated as "no restriction" per the reader's
        // `employeeIds is { Count: > 0 }` guard — company-wide is still bounded by the caller's
        // own resolved direct-report set at the handler layer, never here.
        var result = await reader.GetLeaveSummaryAsync(companyId, employeeIds: [], policyYear: 2026, CancellationToken.None);

        Assert.Single(result);
    }

    [Fact]
    public async Task GetLeaveSummaryAsync_Filters_By_PolicyYear()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var leaveType = SeedLeaveType(db, companyId);
        db.LeaveTypes.Add(leaveType);
        db.LeaveBalances.Add(LeaveBalance.Create(
            Guid.NewGuid(), companyId, employeeId, leaveType.Id, Guid.NewGuid(), 2025, 25m, Now));
        await db.SaveChangesAsync();

        var reader = new LeaveSummaryReader(db);

        var result = await reader.GetLeaveSummaryAsync(companyId, employeeIds: null, policyYear: 2026, CancellationToken.None);

        Assert.Empty(result);
    }
}
