using HR.Modules.Employees.Features.GetEmployeeAuditHistory;
using HR.Modules.Employees.Tests.Infrastructure;
using HR.SharedKernel;

namespace HR.Modules.Employees.Tests;

public class GetEmployeeAuditHistoryHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 8, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task HandleAsync_Resolves_Known_ActorEmployeeId_To_Full_Name()
    {
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var actorId = Guid.NewGuid();

        var reader = new FakeAuditHistoryReader([
            new AuditHistoryEntry(Now, "employee.compensation.created", "Compensation", null, actorId, "Compensation record created", null, null)
        ]);
        var names = new FakeEmployeeNameReader(new Dictionary<Guid, string> { [actorId] = "Alice Smith" });
        var handler = new GetEmployeeAuditHistoryHandler(reader, names);

        var result = await handler.HandleAsync(companyId, employeeId, CancellationToken.None);

        Assert.True(result.IsSuccess);
        var item = Assert.Single(result.Value!.Items);
        Assert.Equal("Alice Smith", item.User);
    }

    [Fact]
    public async Task HandleAsync_Returns_System_When_ActorEmployeeId_Is_Null()
    {
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        var reader = new FakeAuditHistoryReader([
            new AuditHistoryEntry(Now, "document.expired", "EmployeeDocument", null, null, "Document expired", null, null)
        ]);
        var handler = new GetEmployeeAuditHistoryHandler(reader, new FakeEmployeeNameReader());

        var result = await handler.HandleAsync(companyId, employeeId, CancellationToken.None);

        Assert.True(result.IsSuccess);
        var item = Assert.Single(result.Value!.Items);
        Assert.Equal("System", item.User);
    }

    [Fact]
    public async Task HandleAsync_Returns_Unknown_When_ActorEmployeeId_Not_Found_In_Name_Reader()
    {
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var actorId = Guid.NewGuid();

        var reader = new FakeAuditHistoryReader([
            new AuditHistoryEntry(Now, "leave.approved", "LeaveRequest", null, actorId, "Leave request approved", null, null)
        ]);
        // No names configured — actorId will not resolve.
        var handler = new GetEmployeeAuditHistoryHandler(reader, new FakeEmployeeNameReader());

        var result = await handler.HandleAsync(companyId, employeeId, CancellationToken.None);

        Assert.True(result.IsSuccess);
        var item = Assert.Single(result.Value!.Items);
        Assert.Equal("Unknown", item.User);
    }

    [Theory]
    [InlineData("Compensation", "Employees")]
    [InlineData("LeaveRequest", "Leave")]
    [InlineData("SicknessRecord", "Sickness")]
    [InlineData("ProbationRecord", "Probation")]
    [InlineData("EmployeeDocument", "Documents")]
    [InlineData("AssetAssignment", "Assets")]
    public async Task HandleAsync_Maps_EntityType_To_Expected_Module(string entityType, string expectedModule)
    {
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        var reader = new FakeAuditHistoryReader([
            new AuditHistoryEntry(Now, "some.event", entityType, null, null, "Something happened", null, null)
        ]);
        var handler = new GetEmployeeAuditHistoryHandler(reader, new FakeEmployeeNameReader());

        var result = await handler.HandleAsync(companyId, employeeId, CancellationToken.None);

        Assert.True(result.IsSuccess);
        var item = Assert.Single(result.Value!.Items);
        Assert.Equal(expectedModule, item.Module);
    }

    [Fact]
    public async Task HandleAsync_Falls_Back_To_Raw_EntityType_When_Unmapped()
    {
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        var reader = new FakeAuditHistoryReader([
            new AuditHistoryEntry(Now, "notification.sent", "Notification", null, null, "Notification sent", null, null)
        ]);
        var handler = new GetEmployeeAuditHistoryHandler(reader, new FakeEmployeeNameReader());

        var result = await handler.HandleAsync(companyId, employeeId, CancellationToken.None);

        Assert.True(result.IsSuccess);
        var item = Assert.Single(result.Value!.Items);
        Assert.Equal("Notification", item.Module);
    }

    [Fact]
    public async Task HandleAsync_Uses_Summary_As_Action_When_Present()
    {
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        var reader = new FakeAuditHistoryReader([
            new AuditHistoryEntry(Now, "employee.compensation.created", "Compensation", null, null, "Compensation record created", null, null)
        ]);
        var handler = new GetEmployeeAuditHistoryHandler(reader, new FakeEmployeeNameReader());

        var result = await handler.HandleAsync(companyId, employeeId, CancellationToken.None);

        Assert.True(result.IsSuccess);
        var item = Assert.Single(result.Value!.Items);
        Assert.Equal("Compensation record created", item.Action);
    }

    [Fact]
    public async Task HandleAsync_Falls_Back_To_EventType_As_Action_When_Summary_Is_Null()
    {
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        var reader = new FakeAuditHistoryReader([
            new AuditHistoryEntry(Now, "employee.compensation.created", "Compensation", null, null, null, null, null)
        ]);
        var handler = new GetEmployeeAuditHistoryHandler(reader, new FakeEmployeeNameReader());

        var result = await handler.HandleAsync(companyId, employeeId, CancellationToken.None);

        Assert.True(result.IsSuccess);
        var item = Assert.Single(result.Value!.Items);
        Assert.Equal("employee.compensation.created", item.Action);
    }

    [Fact]
    public async Task HandleAsync_Builds_Changes_From_AfterJson_Only_When_BeforeJson_Is_Null()
    {
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        var reader = new FakeAuditHistoryReader([
            new AuditHistoryEntry(
                Now, "employee.compensation.created", "Compensation", null, null, "Compensation record created",
                null, """{"EffectiveFrom":"2027-01-01","Salary":45000}""")
        ]);
        var handler = new GetEmployeeAuditHistoryHandler(reader, new FakeEmployeeNameReader());

        var result = await handler.HandleAsync(companyId, employeeId, CancellationToken.None);

        Assert.True(result.IsSuccess);
        var item = Assert.Single(result.Value!.Items);
        Assert.Equal(2, item.Changes.Count);

        var effectiveFrom = item.Changes.Single(c => c.Field == "Effective From");
        Assert.Equal("—", effectiveFrom.Before);
        Assert.Equal("2027-01-01", effectiveFrom.After);

        var salary = item.Changes.Single(c => c.Field == "Salary");
        Assert.Equal("—", salary.Before);
        Assert.Equal("45000", salary.After);
    }

    [Fact]
    public async Task HandleAsync_Builds_Changes_Capturing_Distinct_Before_And_After_Values_For_Same_Field()
    {
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        var reader = new FakeAuditHistoryReader([
            new AuditHistoryEntry(
                Now, "employee.compensation.reopened", "Compensation", null, null, "Compensation record reopened",
                """{"EffectiveTo":"2026-12-31"}""", """{"EffectiveTo":null}""")
        ]);
        var handler = new GetEmployeeAuditHistoryHandler(reader, new FakeEmployeeNameReader());

        var result = await handler.HandleAsync(companyId, employeeId, CancellationToken.None);

        Assert.True(result.IsSuccess);
        var item = Assert.Single(result.Value!.Items);
        var change = Assert.Single(item.Changes);

        Assert.Equal("Effective To", change.Field);
        Assert.Equal("2026-12-31", change.Before);
        Assert.Equal("—", change.After);
    }

    [Fact]
    public async Task HandleAsync_Returns_Empty_Changes_When_BeforeJson_And_AfterJson_Are_Both_Null()
    {
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        var reader = new FakeAuditHistoryReader([
            new AuditHistoryEntry(Now, "notification.sent", "Notification", null, null, "Notification sent", null, null)
        ]);
        var handler = new GetEmployeeAuditHistoryHandler(reader, new FakeEmployeeNameReader());

        var result = await handler.HandleAsync(companyId, employeeId, CancellationToken.None);

        Assert.True(result.IsSuccess);
        var item = Assert.Single(result.Value!.Items);
        Assert.Empty(item.Changes);
    }

    [Fact]
    public async Task HandleAsync_Returns_Empty_Response_When_Reader_Returns_No_Entries()
    {
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        var reader = new FakeAuditHistoryReader([]);
        var handler = new GetEmployeeAuditHistoryHandler(reader, new FakeEmployeeNameReader());

        var result = await handler.HandleAsync(companyId, employeeId, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value!.Items);
    }
}
