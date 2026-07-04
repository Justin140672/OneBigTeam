using HR.Modules.Leave.Domain;
using HR.Modules.Leave.Features.ListLeaveTypes;
using HR.Modules.Leave.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Leave.Tests;

public class ListLeaveTypesHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 12, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task HandleAsync_Returns_All_LeaveTypes_Ordered_By_Name_When_IsActive_Filter_Is_Null()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();

        var annual = LeaveType.Create(Guid.NewGuid(), companyId, "Annual Leave", "ANNUAL", 25, AccrualMethod.Monthly, LeaveTypeBehaviour.Standard, Now);
        var sick = LeaveType.Create(Guid.NewGuid(), companyId, "Sick Leave", "SICK", 10, AccrualMethod.None, LeaveTypeBehaviour.Standard, Now);
        sick.Deactivate(Now);
        context.LeaveTypes.AddRange(annual, sick);
        await context.SaveChangesAsync();

        var handler = new ListLeaveTypesHandler(context);
        var result = await handler.HandleAsync(new ListLeaveTypesRequest { CompanyId = companyId }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value!.Items.Count);
        Assert.Equal(["Annual Leave", "Sick Leave"], result.Value.Items.Select(i => i.Name));
    }

    [Fact]
    public async Task HandleAsync_Filters_By_IsActive_When_Specified()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();

        var active = LeaveType.Create(Guid.NewGuid(), companyId, "Annual Leave", "ANNUAL", 25, AccrualMethod.Monthly, LeaveTypeBehaviour.Standard, Now);
        var inactive = LeaveType.Create(Guid.NewGuid(), companyId, "Sick Leave", "SICK", 10, AccrualMethod.None, LeaveTypeBehaviour.Standard, Now);
        inactive.Deactivate(Now);
        context.LeaveTypes.AddRange(active, inactive);
        await context.SaveChangesAsync();

        var handler = new ListLeaveTypesHandler(context);
        var result = await handler.HandleAsync(
            new ListLeaveTypesRequest { CompanyId = companyId, IsActive = true }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value!.Items);
        Assert.Equal("Annual Leave", result.Value.Items[0].Name);
    }

    [Fact]
    public async Task HandleAsync_Isolates_Results_By_Company()
    {
        await using var context = BuildContext();
        var companyA = Guid.NewGuid();
        var companyB = Guid.NewGuid();

        context.LeaveTypes.AddRange(
            LeaveType.Create(Guid.NewGuid(), companyA, "Annual Leave", "ANNUAL", 25, AccrualMethod.Monthly, LeaveTypeBehaviour.Standard, Now),
            LeaveType.Create(Guid.NewGuid(), companyB, "Sick Leave", "SICK", 10, AccrualMethod.None, LeaveTypeBehaviour.Standard, Now));
        await context.SaveChangesAsync();

        var handler = new ListLeaveTypesHandler(context);
        var result = await handler.HandleAsync(new ListLeaveTypesRequest { CompanyId = companyA }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value!.Items);
        Assert.Equal("Annual Leave", result.Value.Items[0].Name);
    }

    [Fact]
    public async Task HandleAsync_Returns_Empty_List_When_No_LeaveTypes_Exist()
    {
        await using var context = BuildContext();
        var handler = new ListLeaveTypesHandler(context);

        var result = await handler.HandleAsync(new ListLeaveTypesRequest { CompanyId = Guid.NewGuid() }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value!.Items);
    }

    private static LeaveDbContext BuildContext()
    {
        var options = new DbContextOptionsBuilder<LeaveDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new LeaveDbContext(options);
    }
}
