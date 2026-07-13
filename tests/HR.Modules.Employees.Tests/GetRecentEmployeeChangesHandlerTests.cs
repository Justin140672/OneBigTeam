using HR.Modules.Employees.Features.GetRecentEmployeeChanges;
using HR.Modules.Employees.Tests.Infrastructure;
using HR.SharedKernel;

namespace HR.Modules.Employees.Tests;

public class GetRecentEmployeeChangesHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 8, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task HandleAsync_Returns_Empty_Response_When_Reader_Returns_No_Entries()
    {
        var companyId = Guid.NewGuid();

        var reader = new FakeAuditHistoryReader([]);
        var handler = new GetRecentEmployeeChangesHandler(reader, new FakeEmployeeNameReader());

        var result = await handler.HandleAsync(companyId, CancellationToken.None);

        Assert.Empty(result.Items);
    }

    [Fact]
    public async Task HandleAsync_Resolves_Known_EmployeeId_And_ActorEmployeeId_To_Full_Names()
    {
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var actorId = Guid.NewGuid();

        var reader = new FakeAuditHistoryReader([
            new AuditHistoryEntry(Now, "EmployeeUpdated", "Employee", null, actorId, "Contact details updated", null, null, employeeId)
        ]);
        var names = new FakeEmployeeNameReader(new Dictionary<Guid, string>
        {
            [employeeId] = "Jane Doe",
            [actorId] = "Alice Smith"
        });
        var handler = new GetRecentEmployeeChangesHandler(reader, names);

        var result = await handler.HandleAsync(companyId, CancellationToken.None);

        var item = Assert.Single(result.Items);
        Assert.Equal(Now, item.OccurredAt);
        Assert.Equal("Jane Doe", item.EmployeeName);
        Assert.Equal("Contact details updated", item.Action);
        Assert.Equal("Alice Smith", item.ActorName);
    }

    [Fact]
    public async Task HandleAsync_Returns_Unknown_When_EmployeeId_Not_Found_In_Name_Reader()
    {
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        var reader = new FakeAuditHistoryReader([
            new AuditHistoryEntry(Now, "EmployeeUpdated", "Employee", null, null, "Something changed", null, null, employeeId)
        ]);
        // No names configured — employeeId will not resolve.
        var handler = new GetRecentEmployeeChangesHandler(reader, new FakeEmployeeNameReader());

        var result = await handler.HandleAsync(companyId, CancellationToken.None);

        var item = Assert.Single(result.Items);
        Assert.Equal("Unknown", item.EmployeeName);
    }

    [Fact]
    public async Task HandleAsync_Returns_System_When_ActorEmployeeId_Is_Null()
    {
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        var reader = new FakeAuditHistoryReader([
            new AuditHistoryEntry(Now, "EmployeeCreated", "Employee", null, null, "Employee record created", null, null, employeeId)
        ]);
        var names = new FakeEmployeeNameReader(new Dictionary<Guid, string> { [employeeId] = "Jane Doe" });
        var handler = new GetRecentEmployeeChangesHandler(reader, names);

        var result = await handler.HandleAsync(companyId, CancellationToken.None);

        var item = Assert.Single(result.Items);
        Assert.Equal("System", item.ActorName);
    }

    [Fact]
    public async Task HandleAsync_Uses_Summary_As_Action_When_Present()
    {
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        var reader = new FakeAuditHistoryReader([
            new AuditHistoryEntry(Now, "EmployeeCreated", "Employee", null, null, "Employee record created", null, null, employeeId)
        ]);
        var handler = new GetRecentEmployeeChangesHandler(reader, new FakeEmployeeNameReader());

        var result = await handler.HandleAsync(companyId, CancellationToken.None);

        var item = Assert.Single(result.Items);
        Assert.Equal("Employee record created", item.Action);
    }

    [Fact]
    public async Task HandleAsync_Falls_Back_To_Humanized_EventType_As_Action_When_Summary_Is_Null()
    {
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        var reader = new FakeAuditHistoryReader([
            new AuditHistoryEntry(Now, "EmployeeCreated", "Employee", null, null, null, null, null, employeeId)
        ]);
        var handler = new GetRecentEmployeeChangesHandler(reader, new FakeEmployeeNameReader());

        var result = await handler.HandleAsync(companyId, CancellationToken.None);

        var item = Assert.Single(result.Items);
        Assert.Equal("Employee Created", item.Action);
    }

    [Fact]
    public async Task HandleAsync_Falls_Back_To_Humanized_EventType_As_Action_When_Summary_Is_Empty()
    {
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        var reader = new FakeAuditHistoryReader([
            new AuditHistoryEntry(Now, "EmployeeTerminated", "Employee", null, null, string.Empty, null, null, employeeId)
        ]);
        var handler = new GetRecentEmployeeChangesHandler(reader, new FakeEmployeeNameReader());

        var result = await handler.HandleAsync(companyId, CancellationToken.None);

        var item = Assert.Single(result.Items);
        Assert.Equal("Employee Terminated", item.Action);
    }
}
