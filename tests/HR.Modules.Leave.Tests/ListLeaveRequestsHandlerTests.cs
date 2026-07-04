using HR.Modules.Leave.Domain;
using HR.Modules.Leave.Features.ListLeaveRequests;
using HR.Modules.Leave.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Leave.Tests;

public class ListLeaveRequestsHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 12, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task HandleAsync_Returns_Requests_Ordered_By_StartDate_Descending()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        var leaveType = LeaveType.Create(Guid.NewGuid(), companyId, "Annual Leave", "ANNUAL", 25, AccrualMethod.Monthly, LeaveTypeBehaviour.Standard, Now);
        context.LeaveTypes.Add(leaveType);

        var earlier = LeaveRequest.Create(Guid.NewGuid(), companyId, employeeId, leaveType.Id, Guid.NewGuid(),
            new DateOnly(2026, 7, 1), LeaveDayPart.FullDay, new DateOnly(2026, 7, 3), LeaveDayPart.FullDay, 3m, "Earlier", Now);
        var later = LeaveRequest.Create(Guid.NewGuid(), companyId, employeeId, leaveType.Id, Guid.NewGuid(),
            new DateOnly(2026, 8, 1), LeaveDayPart.FullDay, new DateOnly(2026, 8, 3), LeaveDayPart.FullDay, 3m, "Later", Now);
        context.LeaveRequests.AddRange(earlier, later);
        await context.SaveChangesAsync();

        var handler = new ListLeaveRequestsHandler(context);
        var result = await handler.HandleAsync(
            new ListLeaveRequestsRequest { CompanyId = companyId, EmployeeId = employeeId }, CancellationToken.None);

        Assert.Equal(2, result.Items.Count);
        Assert.Equal("Later", result.Items[0].Reason);
        Assert.Equal("Earlier", result.Items[1].Reason);
        Assert.Equal("Annual Leave", result.Items[0].LeaveTypeName);
    }

    [Fact]
    public async Task HandleAsync_Returns_Empty_List_When_No_Requests_Exist()
    {
        await using var context = BuildContext();
        var handler = new ListLeaveRequestsHandler(context);

        var result = await handler.HandleAsync(
            new ListLeaveRequestsRequest { CompanyId = Guid.NewGuid(), EmployeeId = Guid.NewGuid() }, CancellationToken.None);

        Assert.Empty(result.Items);
    }

    [Fact]
    public async Task HandleAsync_Does_Not_Return_Another_Employees_Requests()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var otherEmployeeId = Guid.NewGuid();

        var leaveType = LeaveType.Create(Guid.NewGuid(), companyId, "Annual Leave", "ANNUAL", 25, AccrualMethod.Monthly, LeaveTypeBehaviour.Standard, Now);
        context.LeaveTypes.Add(leaveType);

        var otherRequest = LeaveRequest.Create(Guid.NewGuid(), companyId, otherEmployeeId, leaveType.Id, Guid.NewGuid(),
            new DateOnly(2026, 7, 1), LeaveDayPart.FullDay, new DateOnly(2026, 7, 3), LeaveDayPart.FullDay, 3m, "Other", Now);
        context.LeaveRequests.Add(otherRequest);
        await context.SaveChangesAsync();

        var handler = new ListLeaveRequestsHandler(context);
        var result = await handler.HandleAsync(
            new ListLeaveRequestsRequest { CompanyId = companyId, EmployeeId = employeeId }, CancellationToken.None);

        Assert.Empty(result.Items);
    }

    private static LeaveDbContext BuildContext()
    {
        var options = new DbContextOptionsBuilder<LeaveDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new LeaveDbContext(options);
    }
}
