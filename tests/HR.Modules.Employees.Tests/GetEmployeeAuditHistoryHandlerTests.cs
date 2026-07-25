using HR.Modules.Employees.Domain;
using HR.Modules.Employees.Features.GetEmployeeAuditHistory;
using HR.Modules.Employees.Persistence;
using HR.Modules.Employees.Tests.Infrastructure;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Employees.Tests;

public class GetEmployeeAuditHistoryHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 8, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task HandleAsync_Resolves_Known_ActorEmployeeId_To_Full_Name()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var actorId = Guid.NewGuid();

        var reader = new FakeAuditHistoryReader([
            new AuditHistoryEntry(Now, "employee.compensation.created", "Compensation", null, actorId, "Compensation record created", null, null)
        ]);
        var names = new FakeEmployeeNameReader(new Dictionary<Guid, string> { [actorId] = "Alice Smith" });
        var handler = new GetEmployeeAuditHistoryHandler(reader, names, context);

        var result = await handler.HandleAsync(companyId, employeeId, CancellationToken.None);

        Assert.True(result.IsSuccess);
        var item = Assert.Single(result.Value!.Items);
        Assert.Equal("Alice Smith", item.User);
    }

    [Fact]
    public async Task HandleAsync_Returns_System_When_ActorEmployeeId_Is_Null()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        var reader = new FakeAuditHistoryReader([
            new AuditHistoryEntry(Now, "document.expired", "EmployeeDocument", null, null, "Document expired", null, null)
        ]);
        var handler = new GetEmployeeAuditHistoryHandler(reader, new FakeEmployeeNameReader(), context);

        var result = await handler.HandleAsync(companyId, employeeId, CancellationToken.None);

        Assert.True(result.IsSuccess);
        var item = Assert.Single(result.Value!.Items);
        Assert.Equal("System", item.User);
    }

    [Fact]
    public async Task HandleAsync_Returns_Unknown_When_ActorEmployeeId_Not_Found_In_Name_Reader()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var actorId = Guid.NewGuid();

        var reader = new FakeAuditHistoryReader([
            new AuditHistoryEntry(Now, "leave.approved", "LeaveRequest", null, actorId, "Leave request approved", null, null)
        ]);
        // No names configured — actorId will not resolve.
        var handler = new GetEmployeeAuditHistoryHandler(reader, new FakeEmployeeNameReader(), context);

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
    [InlineData("Candidate", "Recruitment")]
    public async Task HandleAsync_Maps_EntityType_To_Expected_Module(string entityType, string expectedModule)
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        var reader = new FakeAuditHistoryReader([
            new AuditHistoryEntry(Now, "some.event", entityType, null, null, "Something happened", null, null)
        ]);
        var handler = new GetEmployeeAuditHistoryHandler(reader, new FakeEmployeeNameReader(), context);

        var result = await handler.HandleAsync(companyId, employeeId, CancellationToken.None);

        Assert.True(result.IsSuccess);
        var item = Assert.Single(result.Value!.Items);
        Assert.Equal(expectedModule, item.Module);
    }

    [Fact]
    public async Task HandleAsync_Falls_Back_To_Raw_EntityType_When_Unmapped()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        var reader = new FakeAuditHistoryReader([
            new AuditHistoryEntry(Now, "notification.sent", "Notification", null, null, "Notification sent", null, null)
        ]);
        var handler = new GetEmployeeAuditHistoryHandler(reader, new FakeEmployeeNameReader(), context);

        var result = await handler.HandleAsync(companyId, employeeId, CancellationToken.None);

        Assert.True(result.IsSuccess);
        var item = Assert.Single(result.Value!.Items);
        Assert.Equal("Notification", item.Module);
    }

    [Fact]
    public async Task HandleAsync_Uses_Summary_As_Action_When_Present()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        var reader = new FakeAuditHistoryReader([
            new AuditHistoryEntry(Now, "employee.compensation.created", "Compensation", null, null, "Compensation record created", null, null)
        ]);
        var handler = new GetEmployeeAuditHistoryHandler(reader, new FakeEmployeeNameReader(), context);

        var result = await handler.HandleAsync(companyId, employeeId, CancellationToken.None);

        Assert.True(result.IsSuccess);
        var item = Assert.Single(result.Value!.Items);
        Assert.Equal("Compensation record created", item.Action);
    }

    [Fact]
    public async Task HandleAsync_Falls_Back_To_EventType_As_Action_When_Summary_Is_Null()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        var reader = new FakeAuditHistoryReader([
            new AuditHistoryEntry(Now, "employee.compensation.created", "Compensation", null, null, null, null, null)
        ]);
        var handler = new GetEmployeeAuditHistoryHandler(reader, new FakeEmployeeNameReader(), context);

        var result = await handler.HandleAsync(companyId, employeeId, CancellationToken.None);

        Assert.True(result.IsSuccess);
        var item = Assert.Single(result.Value!.Items);
        Assert.Equal("employee.compensation.created", item.Action);
    }

    [Fact]
    public async Task HandleAsync_Builds_Changes_From_AfterJson_Only_When_BeforeJson_Is_Null()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        var reader = new FakeAuditHistoryReader([
            new AuditHistoryEntry(
                Now, "employee.compensation.created", "Compensation", null, null, "Compensation record created",
                null, """{"EffectiveFrom":"2027-01-01","Salary":45000}""")
        ]);
        var handler = new GetEmployeeAuditHistoryHandler(reader, new FakeEmployeeNameReader(), context);

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
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        var reader = new FakeAuditHistoryReader([
            new AuditHistoryEntry(
                Now, "employee.compensation.reopened", "Compensation", null, null, "Compensation record reopened",
                """{"EffectiveTo":"2026-12-31"}""", """{"EffectiveTo":null}""")
        ]);
        var handler = new GetEmployeeAuditHistoryHandler(reader, new FakeEmployeeNameReader(), context);

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
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        var reader = new FakeAuditHistoryReader([
            new AuditHistoryEntry(Now, "notification.sent", "Notification", null, null, "Notification sent", null, null)
        ]);
        var handler = new GetEmployeeAuditHistoryHandler(reader, new FakeEmployeeNameReader(), context);

        var result = await handler.HandleAsync(companyId, employeeId, CancellationToken.None);

        Assert.True(result.IsSuccess);
        var item = Assert.Single(result.Value!.Items);
        Assert.Empty(item.Changes);
    }

    [Fact]
    public async Task HandleAsync_Returns_Empty_Response_When_Reader_Returns_No_Entries()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        var reader = new FakeAuditHistoryReader([]);
        var handler = new GetEmployeeAuditHistoryHandler(reader, new FakeEmployeeNameReader(), context);

        var result = await handler.HandleAsync(companyId, employeeId, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value!.Items);
    }

    // ── DepartmentId / PositionProfileId / LocationId resolution ────────────────

    [Fact]
    public async Task HandleAsync_Resolves_DepartmentId_To_Department_Name()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        var department = Department.Create(Guid.NewGuid(), companyId, "Engineering", null, Now);
        context.Departments.Add(department);
        await context.SaveChangesAsync();

        var reader = new FakeAuditHistoryReader([
            new AuditHistoryEntry(
                Now, "employee.profile.updated", "Employee", null, null, "Employee profile updated",
                null, $$"""{"DepartmentId":"{{department.Id}}"}""")
        ]);
        var handler = new GetEmployeeAuditHistoryHandler(reader, new FakeEmployeeNameReader(), context);

        var result = await handler.HandleAsync(companyId, employeeId, CancellationToken.None);

        Assert.True(result.IsSuccess);
        var item = Assert.Single(result.Value!.Items);
        var change = Assert.Single(item.Changes);
        Assert.Equal("Department Id", change.Field);
        Assert.Equal("Engineering", change.After);
    }

    [Fact]
    public async Task HandleAsync_Resolves_PositionProfileId_To_PositionProfile_Title()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        var positionProfile = PositionProfile.Create(
            Guid.NewGuid(), companyId, Guid.NewGuid(), Guid.NewGuid(), "Engineering Manager", null, null, null, null, null, null, null, Guid.NewGuid(), Now);
        context.PositionProfiles.Add(positionProfile);
        await context.SaveChangesAsync();

        var reader = new FakeAuditHistoryReader([
            new AuditHistoryEntry(
                Now, "employee.profile.updated", "Employee", null, null, "Employee profile updated",
                null, $$"""{"PositionProfileId":"{{positionProfile.Id}}"}""")
        ]);
        var handler = new GetEmployeeAuditHistoryHandler(reader, new FakeEmployeeNameReader(), context);

        var result = await handler.HandleAsync(companyId, employeeId, CancellationToken.None);

        Assert.True(result.IsSuccess);
        var item = Assert.Single(result.Value!.Items);
        var change = Assert.Single(item.Changes);
        Assert.Equal("Position Profile Id", change.Field);
        Assert.Equal("Engineering Manager", change.After);
    }

    [Fact]
    public async Task HandleAsync_Resolves_LocationId_To_Location_Name()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        var location = Location.Create(Guid.NewGuid(), companyId, Guid.NewGuid(), "London Office", null, Now);
        context.Locations.Add(location);
        await context.SaveChangesAsync();

        var reader = new FakeAuditHistoryReader([
            new AuditHistoryEntry(
                Now, "employee.profile.updated", "Employee", null, null, "Employee profile updated",
                null, $$"""{"LocationId":"{{location.Id}}"}""")
        ]);
        var handler = new GetEmployeeAuditHistoryHandler(reader, new FakeEmployeeNameReader(), context);

        var result = await handler.HandleAsync(companyId, employeeId, CancellationToken.None);

        Assert.True(result.IsSuccess);
        var item = Assert.Single(result.Value!.Items);
        var change = Assert.Single(item.Changes);
        Assert.Equal("Location Id", change.Field);
        Assert.Equal("London Office", change.After);
    }

    [Fact]
    public async Task HandleAsync_Resolves_Unmatched_DepartmentId_To_Unknown()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var unknownDepartmentId = Guid.NewGuid();

        var reader = new FakeAuditHistoryReader([
            new AuditHistoryEntry(
                Now, "employee.profile.updated", "Employee", null, null, "Employee profile updated",
                null, $$"""{"DepartmentId":"{{unknownDepartmentId}}"}""")
        ]);
        var handler = new GetEmployeeAuditHistoryHandler(reader, new FakeEmployeeNameReader(), context);

        var result = await handler.HandleAsync(companyId, employeeId, CancellationToken.None);

        Assert.True(result.IsSuccess);
        var item = Assert.Single(result.Value!.Items);
        var change = Assert.Single(item.Changes);
        Assert.Equal("Unknown", change.After);
    }

    [Fact]
    public async Task HandleAsync_Does_Not_Resolve_DepartmentId_From_A_Different_Company()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var otherCompanyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        var department = Department.Create(Guid.NewGuid(), otherCompanyId, "Engineering", null, Now);
        context.Departments.Add(department);
        await context.SaveChangesAsync();

        var reader = new FakeAuditHistoryReader([
            new AuditHistoryEntry(
                Now, "employee.profile.updated", "Employee", null, null, "Employee profile updated",
                null, $$"""{"DepartmentId":"{{department.Id}}"}""")
        ]);
        var handler = new GetEmployeeAuditHistoryHandler(reader, new FakeEmployeeNameReader(), context);

        var result = await handler.HandleAsync(companyId, employeeId, CancellationToken.None);

        Assert.True(result.IsSuccess);
        var item = Assert.Single(result.Value!.Items);
        var change = Assert.Single(item.Changes);
        Assert.Equal("Unknown", change.After);
    }

    [Fact]
    public async Task HandleAsync_Does_Not_Resolve_Guid_Values_For_Other_Field_Names()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        var department = Department.Create(Guid.NewGuid(), companyId, "Engineering", null, Now);
        context.Departments.Add(department);
        await context.SaveChangesAsync();

        // "ManagerId" is not one of the three resolved field names, even though its value is a
        // Department id and happens to be GUID-shaped — it must render as the raw guid string.
        var reader = new FakeAuditHistoryReader([
            new AuditHistoryEntry(
                Now, "employee.profile.updated", "Employee", null, null, "Employee profile updated",
                null, $$"""{"ManagerId":"{{department.Id}}"}""")
        ]);
        var handler = new GetEmployeeAuditHistoryHandler(reader, new FakeEmployeeNameReader(), context);

        var result = await handler.HandleAsync(companyId, employeeId, CancellationToken.None);

        Assert.True(result.IsSuccess);
        var item = Assert.Single(result.Value!.Items);
        var change = Assert.Single(item.Changes);
        Assert.Equal("Manager Id", change.Field);
        Assert.Equal(department.Id.ToString(), change.After);
    }

    [Fact]
    public async Task HandleAsync_Handles_NonGuid_Values_For_Resolved_Field_Names_Without_Throwing()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        var reader = new FakeAuditHistoryReader([
            new AuditHistoryEntry(
                Now, "employee.profile.updated", "Employee", null, null, "Employee profile updated",
                null, """{"DepartmentId":"not-a-guid"}""")
        ]);
        var handler = new GetEmployeeAuditHistoryHandler(reader, new FakeEmployeeNameReader(), context);

        var result = await handler.HandleAsync(companyId, employeeId, CancellationToken.None);

        Assert.True(result.IsSuccess);
        var item = Assert.Single(result.Value!.Items);
        var change = Assert.Single(item.Changes);
        Assert.Equal("not-a-guid", change.After);
    }

    [Fact]
    public async Task HandleAsync_Handles_Null_Value_For_Resolved_Field_Names_Without_Throwing()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        var reader = new FakeAuditHistoryReader([
            new AuditHistoryEntry(
                Now, "employee.profile.updated", "Employee", null, null, "Employee profile updated",
                null, """{"DepartmentId":null}""")
        ]);
        var handler = new GetEmployeeAuditHistoryHandler(reader, new FakeEmployeeNameReader(), context);

        var result = await handler.HandleAsync(companyId, employeeId, CancellationToken.None);

        Assert.True(result.IsSuccess);
        var item = Assert.Single(result.Value!.Items);
        var change = Assert.Single(item.Changes);
        Assert.Equal("—", change.After);
    }

    // ── Reason field humanization ────────────────────────────────────────────────

    [Theory]
    [InlineData("AnnualReview", "Annual Review")]
    [InlineData("NewHire", "New Hire")]
    [InlineData("MarketAdjustment", "Market Adjustment")]
    [InlineData("RoleChange", "Role Change")]
    [InlineData("Promotion", "Promotion")]
    [InlineData("Correction", "Correction")]
    [InlineData("Other", "Other")]
    public async Task HandleAsync_Humanizes_Reason_Field_Value(string rawReason, string expectedLabel)
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        var reader = new FakeAuditHistoryReader([
            new AuditHistoryEntry(
                Now, "employee.compensation.created", "Compensation", null, null, "Compensation record created",
                null, $$"""{"Reason":"{{rawReason}}"}""")
        ]);
        var handler = new GetEmployeeAuditHistoryHandler(reader, new FakeEmployeeNameReader(), context);

        var result = await handler.HandleAsync(companyId, employeeId, CancellationToken.None);

        Assert.True(result.IsSuccess);
        var item = Assert.Single(result.Value!.Items);
        var change = Assert.Single(item.Changes);
        Assert.Equal("Reason", change.Field);
        Assert.Equal(expectedLabel, change.After);
    }

    [Fact]
    public async Task HandleAsync_Handles_Null_Reason_Value_Without_Throwing()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        var reader = new FakeAuditHistoryReader([
            new AuditHistoryEntry(
                Now, "employee.compensation.created", "Compensation", null, null, "Compensation record created",
                null, """{"Reason":null}""")
        ]);
        var handler = new GetEmployeeAuditHistoryHandler(reader, new FakeEmployeeNameReader(), context);

        var result = await handler.HandleAsync(companyId, employeeId, CancellationToken.None);

        Assert.True(result.IsSuccess);
        var item = Assert.Single(result.Value!.Items);
        var change = Assert.Single(item.Changes);
        Assert.Equal("—", change.After);
    }

    private static EmployeesDbContext BuildContext()
    {
        var options = new DbContextOptionsBuilder<EmployeesDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        return new EmployeesDbContext(options);
    }
}
