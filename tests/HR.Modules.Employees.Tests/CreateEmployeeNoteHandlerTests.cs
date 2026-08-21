using HR.Modules.Employees.Domain;
using HR.Modules.Employees.Features.CreateEmployeeNote;
using HR.Modules.Employees.Persistence;
using HR.Modules.Employees.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Employees.Tests;

public class CreateEmployeeNoteHandlerTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 6, 8, 10, 0, 0, DateTimeKind.Utc);
    private static readonly Guid ActorEmployeeId = Guid.NewGuid();
    private static readonly Guid ActorUserId = Guid.NewGuid();

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_Employee_Does_Not_Exist()
    {
        await using var context = BuildContext();
        var publisher = new FakeAuditPublisher();
        var handler = new CreateEmployeeNoteHandler(context, new FakeClock(FixedUtcNow), publisher);

        var result = await handler.HandleAsync(
            new CreateEmployeeNoteRequest(Guid.NewGuid(), Guid.NewGuid(), NoteCategory.General, "Some note.", false),
            ActorEmployeeId,
            ActorUserId,
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
        Assert.Empty(publisher.Published);
    }

    [Fact]
    public async Task HandleAsync_Creates_Note_And_Publishes_AuditEvent()
    {
        await using var context = BuildContext();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);
        var companyId = Guid.NewGuid();
        var employee = Employee.Create(Guid.NewGuid(), companyId, "Alice", "Smith", "alice@example.com", new DateOnly(2024, 1, 1), true, new DateOnly(1990, 1, 1), "British", "Prefer not to say", "EMP-0001", Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), now);
        context.Employees.Add(employee);
        await context.SaveChangesAsync();

        var publisher = new FakeAuditPublisher();
        var handler = new CreateEmployeeNoteHandler(context, new FakeClock(FixedUtcNow), publisher);

        var result = await handler.HandleAsync(
            new CreateEmployeeNoteRequest(companyId, employee.Id, NoteCategory.Performance, "  Great quarter.  ", true),
            ActorEmployeeId,
            ActorUserId,
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(companyId, result.Value!.CompanyId);
        Assert.Equal(employee.Id, result.Value.EmployeeId);
        Assert.Equal("Performance", result.Value.Category);
        Assert.Equal("Great quarter.", result.Value.NoteText);
        Assert.True(result.Value.IsImportant);
        Assert.False(result.Value.IsSuperseded);
        Assert.Null(result.Value.SupersededByNoteId);
        Assert.Equal(ActorUserId, result.Value.CreatedByUserId);

        var saved = await context.EmployeeNotes.SingleAsync();
        Assert.Equal(result.Value.Id, saved.Id);
        Assert.Equal("Great quarter.", saved.NoteText);

        var evt = Assert.IsType<EmployeeNoteCreatedAuditEvent>(Assert.Single(publisher.Published));
        Assert.Equal(companyId, evt.CompanyId);
        Assert.Equal(employee.Id, evt.EmployeeId);
        Assert.Equal(saved.Id, evt.NoteId);
        Assert.Equal("Performance", evt.Category);
        Assert.True(evt.IsImportant);
        Assert.Equal(ActorUserId, evt.ActorUserId);
        Assert.Equal(ActorEmployeeId, evt.ActorEmployeeId);

        // Confidentiality: the raw note text must never appear anywhere in the published audit
        // event's payload — assert it across the entire event, not just a specific property, so
        // this test still catches a regression if NoteText were ever added to Before/After/Metadata.
        var eventJson = System.Text.Json.JsonSerializer.Serialize(evt);
        Assert.DoesNotContain("Great quarter.", eventJson);
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    internal async Task HandleAsync_Does_Not_Write_Timeline_Entry_For_Note_Creation(
        int categoryValue, bool isImportant)
    {
        var category = (NoteCategory)categoryValue;
        await using var context = BuildContext();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);
        var companyId = Guid.NewGuid();
        var employee = Employee.Create(Guid.NewGuid(), companyId, "Alice", "Smith", "alice@example.com", new DateOnly(2024, 1, 1), true, new DateOnly(1990, 1, 1), "British", "Prefer not to say", "EMP-0001", Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), now);
        context.Employees.Add(employee);
        await context.SaveChangesAsync();

        var handler = new CreateEmployeeNoteHandler(context, new FakeClock(FixedUtcNow), new FakeAuditPublisher());

        var confidentialText = "Highly confidential performance concerns about the employee.";
        var result = await handler.HandleAsync(
            new CreateEmployeeNoteRequest(companyId, employee.Id, category, confidentialText, isImportant),
            ActorEmployeeId,
            ActorUserId,
            CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task HandleAsync_Does_Not_Publish_Event_When_Employee_Does_Not_Exist()
    {
        await using var context = BuildContext();
        var publisher = new FakeAuditPublisher();
        var handler = new CreateEmployeeNoteHandler(context, new FakeClock(FixedUtcNow), publisher);

        await handler.HandleAsync(
            new CreateEmployeeNoteRequest(Guid.NewGuid(), Guid.NewGuid(), NoteCategory.General, "Some note.", false),
            ActorEmployeeId,
            ActorUserId,
            CancellationToken.None);

        Assert.Empty(publisher.Published);
        Assert.Empty(await context.EmployeeNotes.ToListAsync());
    }

    private static EmployeesDbContext BuildContext()
    {
        var options = new DbContextOptionsBuilder<EmployeesDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        return new EmployeesDbContext(options);
    }
}
