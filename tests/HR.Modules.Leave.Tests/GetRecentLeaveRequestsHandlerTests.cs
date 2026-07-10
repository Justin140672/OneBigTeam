using HR.Modules.Leave.Domain;
using HR.Modules.Leave.Features.GetRecentLeaveRequests;
using HR.Modules.Leave.Persistence;
using HR.Modules.Leave.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Leave.Tests;

public class GetRecentLeaveRequestsHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 9, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task HandleAsync_Returns_Empty_When_No_Requests_Exist()
    {
        await using var context = BuildContext();
        var handler = new GetRecentLeaveRequestsHandler(context, new FakeEmployeeNameReader());

        var result = await handler.HandleAsync(
            new GetRecentLeaveRequestsRequest(Guid.NewGuid(), null),
            CancellationToken.None);

        Assert.Empty(result.Items);
    }

    [Fact]
    public async Task HandleAsync_Orders_By_CreatedAt_Descending()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var leaveType = SeedLeaveType(context, companyId);

        var earliest = CreateRequest(companyId, employeeId, leaveType.Id, Now.AddDays(-3));
        var middle = CreateRequest(companyId, employeeId, leaveType.Id, Now.AddDays(-2));
        var latest = CreateRequest(companyId, employeeId, leaveType.Id, Now.AddDays(-1));
        context.LeaveRequests.AddRange(middle, earliest, latest);
        await context.SaveChangesAsync();

        var handler = new GetRecentLeaveRequestsHandler(context, new FakeEmployeeNameReader());
        var result = await handler.HandleAsync(
            new GetRecentLeaveRequestsRequest(companyId, null),
            CancellationToken.None);

        Assert.Equal(
            [latest.Id, middle.Id, earliest.Id],
            result.Items.Select(i => i.LeaveRequestId).ToArray());
    }

    [Fact]
    public async Task HandleAsync_Defaults_To_Take_Ten_When_Not_Specified()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var leaveType = SeedLeaveType(context, companyId);

        for (var i = 0; i < 12; i++)
        {
            context.LeaveRequests.Add(CreateRequest(companyId, employeeId, leaveType.Id, Now.AddDays(-i)));
        }
        await context.SaveChangesAsync();

        var handler = new GetRecentLeaveRequestsHandler(context, new FakeEmployeeNameReader());
        var result = await handler.HandleAsync(
            new GetRecentLeaveRequestsRequest(companyId, null),
            CancellationToken.None);

        Assert.Equal(10, result.Items.Count);
        // The 10 most recent (smallest AddDays offset -> latest CreatedAt) should be returned.
        Assert.True(result.Items.SequenceEqual(result.Items.OrderByDescending(i => i.CreatedAt)));
        Assert.Equal(Now, result.Items[0].CreatedAt);
    }

    [Fact]
    public async Task HandleAsync_Respects_Explicit_Take_Value()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var leaveType = SeedLeaveType(context, companyId);

        for (var i = 0; i < 5; i++)
        {
            context.LeaveRequests.Add(CreateRequest(companyId, employeeId, leaveType.Id, Now.AddDays(-i)));
        }
        await context.SaveChangesAsync();

        var handler = new GetRecentLeaveRequestsHandler(context, new FakeEmployeeNameReader());
        var result = await handler.HandleAsync(
            new GetRecentLeaveRequestsRequest(companyId, 3),
            CancellationToken.None);

        Assert.Equal(3, result.Items.Count);
        Assert.True(result.Items.SequenceEqual(result.Items.OrderByDescending(i => i.CreatedAt)));
    }

    [Fact]
    public async Task HandleAsync_Resolves_Employee_Names_Via_EmployeeNameReader()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var leaveType = SeedLeaveType(context, companyId);
        context.LeaveRequests.Add(CreateRequest(companyId, employeeId, leaveType.Id, Now));
        await context.SaveChangesAsync();

        var names = new Dictionary<Guid, string> { [employeeId] = "Jane Doe" };
        var handler = new GetRecentLeaveRequestsHandler(context, new FakeEmployeeNameReader(names));

        var result = await handler.HandleAsync(
            new GetRecentLeaveRequestsRequest(companyId, null),
            CancellationToken.None);

        var item = Assert.Single(result.Items);
        Assert.Equal("Jane Doe", item.EmployeeName);
        Assert.Equal(leaveType.Name, item.LeaveTypeName);
        Assert.Equal("Pending", item.Status);
    }

    [Fact]
    public async Task HandleAsync_Falls_Back_To_Unknown_Employee_When_Name_Not_Found()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var leaveType = SeedLeaveType(context, companyId);
        context.LeaveRequests.Add(CreateRequest(companyId, employeeId, leaveType.Id, Now));
        await context.SaveChangesAsync();

        var handler = new GetRecentLeaveRequestsHandler(context, new FakeEmployeeNameReader());
        var result = await handler.HandleAsync(
            new GetRecentLeaveRequestsRequest(companyId, null),
            CancellationToken.None);

        var item = Assert.Single(result.Items);
        Assert.Equal("Unknown Employee", item.EmployeeName);
    }

    [Fact]
    public async Task HandleAsync_Isolates_By_Company()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var otherCompanyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var leaveType = SeedLeaveType(context, companyId);
        var otherLeaveType = SeedLeaveType(context, otherCompanyId);

        context.LeaveRequests.Add(CreateRequest(companyId, employeeId, leaveType.Id, Now));
        context.LeaveRequests.Add(CreateRequest(otherCompanyId, employeeId, otherLeaveType.Id, Now));
        await context.SaveChangesAsync();

        var handler = new GetRecentLeaveRequestsHandler(context, new FakeEmployeeNameReader());
        var result = await handler.HandleAsync(
            new GetRecentLeaveRequestsRequest(companyId, null),
            CancellationToken.None);

        Assert.Single(result.Items);
    }

    private static LeaveType SeedLeaveType(LeaveDbContext context, Guid companyId)
    {
        var leaveType = LeaveType.Create(
            Guid.NewGuid(), companyId, "Annual Leave", "ANNUAL", 25,
            AccrualMethod.Monthly, LeaveTypeBehaviour.Standard, Now);
        context.LeaveTypes.Add(leaveType);
        return leaveType;
    }

    private static LeaveRequest CreateRequest(Guid companyId, Guid employeeId, Guid leaveTypeId, DateTimeOffset createdAt) =>
        LeaveRequest.Create(
            Guid.NewGuid(), companyId, employeeId, leaveTypeId, Guid.NewGuid(),
            new DateOnly(2026, 7, 1), LeaveDayPart.FullDay, new DateOnly(2026, 7, 3), LeaveDayPart.FullDay,
            3m, "Trip", createdAt);

    private static LeaveDbContext BuildContext()
    {
        var options = new DbContextOptionsBuilder<LeaveDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new LeaveDbContext(options);
    }
}
