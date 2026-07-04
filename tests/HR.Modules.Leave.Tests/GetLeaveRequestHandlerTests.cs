using HR.Modules.Leave.Domain;
using HR.Modules.Leave.Features.GetLeaveRequest;
using HR.Modules.Leave.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Leave.Tests;

public class GetLeaveRequestHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 12, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task HandleAsync_Returns_Request_With_LeaveTypeName_When_Found()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        var leaveType = LeaveType.Create(Guid.NewGuid(), companyId, "Annual Leave", "ANNUAL", 25, AccrualMethod.Monthly, LeaveTypeBehaviour.Standard, Now);
        context.LeaveTypes.Add(leaveType);

        var request = LeaveRequest.Create(Guid.NewGuid(), companyId, employeeId, leaveType.Id, Guid.NewGuid(),
            new DateOnly(2026, 8, 3), LeaveDayPart.FullDay, new DateOnly(2026, 8, 7), LeaveDayPart.FullDay, 5m, "Family holiday", Now);
        context.LeaveRequests.Add(request);
        await context.SaveChangesAsync();

        var handler = new GetLeaveRequestHandler(context);
        var result = await handler.HandleAsync(
            new GetLeaveRequestRequest { CompanyId = companyId, EmployeeId = employeeId, Id = request.Id },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Annual Leave", result.Value!.LeaveTypeName);
        Assert.Equal("Pending", result.Value.Status);
        Assert.Equal(5m, result.Value.TotalDays);
        Assert.Equal("Family holiday", result.Value.Reason);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_Request_Does_Not_Exist()
    {
        await using var context = BuildContext();
        var handler = new GetLeaveRequestHandler(context);

        var result = await handler.HandleAsync(
            new GetLeaveRequestRequest { CompanyId = Guid.NewGuid(), EmployeeId = Guid.NewGuid(), Id = Guid.NewGuid() },
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_Request_Belongs_To_Different_Employee()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();

        var leaveType = LeaveType.Create(Guid.NewGuid(), companyId, "Annual Leave", "ANNUAL", 25, AccrualMethod.Monthly, LeaveTypeBehaviour.Standard, Now);
        context.LeaveTypes.Add(leaveType);

        var request = LeaveRequest.Create(Guid.NewGuid(), companyId, Guid.NewGuid(), leaveType.Id, Guid.NewGuid(),
            new DateOnly(2026, 8, 3), LeaveDayPart.FullDay, new DateOnly(2026, 8, 7), LeaveDayPart.FullDay, 5m, null, Now);
        context.LeaveRequests.Add(request);
        await context.SaveChangesAsync();

        var handler = new GetLeaveRequestHandler(context);
        var result = await handler.HandleAsync(
            new GetLeaveRequestRequest { CompanyId = companyId, EmployeeId = Guid.NewGuid(), Id = request.Id },
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    private static LeaveDbContext BuildContext()
    {
        var options = new DbContextOptionsBuilder<LeaveDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new LeaveDbContext(options);
    }
}
