using HR.Modules.Employees.Domain;
using HR.Modules.Employees.Features.SupersedeEmployeeNote;
using HR.Modules.Employees.Persistence;
using HR.Modules.Employees.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Employees.Tests;

public class SupersedeEmployeeNoteHandlerTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 6, 8, 10, 0, 0, DateTimeKind.Utc);
    private static readonly Guid ActorEmployeeId = Guid.NewGuid();
    private static readonly Guid ActorUserId = Guid.NewGuid();

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_Original_Note_Does_Not_Exist()
    {
        await using var context = BuildContext();
        var publisher = new FakeAuditPublisher();
        var handler = new SupersedeEmployeeNoteHandler(context, new FakeClock(FixedUtcNow), publisher);

        var result = await handler.HandleAsync(
            new SupersedeEmployeeNoteRequest(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), NoteCategory.General, "Replacement text.", false),
            ActorEmployeeId,
            ActorUserId,
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
        Assert.Empty(publisher.Published);
    }

    [Fact]
    public async Task HandleAsync_Returns_Conflict_When_Original_Note_Already_Superseded()
    {
        await using var context = BuildContext();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        var original = EmployeeNote.Create(Guid.NewGuid(), companyId, employeeId, NoteCategory.General, "Original text.", false, Guid.NewGuid(), now);
        original.MarkSuperseded(Guid.NewGuid());
        context.EmployeeNotes.Add(original);
        await context.SaveChangesAsync();

        var publisher = new FakeAuditPublisher();
        var handler = new SupersedeEmployeeNoteHandler(context, new FakeClock(FixedUtcNow), publisher);

        var result = await handler.HandleAsync(
            new SupersedeEmployeeNoteRequest(companyId, employeeId, original.Id, NoteCategory.General, "Replacement text.", false),
            ActorEmployeeId,
            ActorUserId,
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("conflict", result.Error.Code);
        Assert.Empty(publisher.Published);
    }

    [Fact]
    public async Task HandleAsync_Creates_Replacement_Note_And_Marks_Original_Superseded()
    {
        await using var context = BuildContext();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        var original = EmployeeNote.Create(Guid.NewGuid(), companyId, employeeId, NoteCategory.Conduct, "Original text.", false, Guid.NewGuid(), now);
        context.EmployeeNotes.Add(original);
        await context.SaveChangesAsync();

        var publisher = new FakeAuditPublisher();
        var handler = new SupersedeEmployeeNoteHandler(context, new FakeClock(FixedUtcNow), publisher);

        var result = await handler.HandleAsync(
            new SupersedeEmployeeNoteRequest(companyId, employeeId, original.Id, NoteCategory.Conduct, "  Corrected text.  ", true),
            ActorEmployeeId,
            ActorUserId,
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Corrected text.", result.Value!.NoteText);
        Assert.True(result.Value.IsImportant);
        Assert.False(result.Value.IsSuperseded);
        Assert.Equal(original.Id, result.Value.OriginalNoteId);
        Assert.True(result.Value.OriginalNoteSuperseded);

        var reloadedOriginal = await context.EmployeeNotes.SingleAsync(n => n.Id == original.Id);
        Assert.True(reloadedOriginal.IsSuperseded);
        Assert.Equal(result.Value.Id, reloadedOriginal.SupersededByNoteId);

        var newNote = await context.EmployeeNotes.SingleAsync(n => n.Id == result.Value.Id);
        Assert.Equal("Corrected text.", newNote.NoteText);
        Assert.False(newNote.IsSuperseded);

        Assert.Equal(2, publisher.Published.Count);

        var createdEvent = Assert.IsType<EmployeeNoteCreatedAuditEvent>(publisher.Published[0]);
        Assert.Equal(newNote.Id, createdEvent.NoteId);
        Assert.Equal(ActorUserId, createdEvent.ActorUserId);
        Assert.Equal(ActorEmployeeId, createdEvent.ActorEmployeeId);

        var supersededEvent = Assert.IsType<EmployeeNoteSupersededAuditEvent>(publisher.Published[1]);
        Assert.Equal(companyId, supersededEvent.CompanyId);
        Assert.Equal(employeeId, supersededEvent.EmployeeId);
        Assert.Equal(original.Id, supersededEvent.OriginalNoteId);
        Assert.Equal(newNote.Id, supersededEvent.NewNoteId);
        Assert.Equal(ActorUserId, supersededEvent.ActorUserId);
        Assert.Equal(ActorEmployeeId, supersededEvent.ActorEmployeeId);
    }

    private static EmployeesDbContext BuildContext()
    {
        var options = new DbContextOptionsBuilder<EmployeesDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        return new EmployeesDbContext(options);
    }
}
