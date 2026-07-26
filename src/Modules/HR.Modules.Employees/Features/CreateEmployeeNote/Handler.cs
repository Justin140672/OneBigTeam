using HR.Infrastructure.Abstractions;
using HR.Modules.Employees.Domain;
using HR.Modules.Employees.Persistence;
using HR.Modules.Employees.Services;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Employees.Features.CreateEmployeeNote;

internal sealed class CreateEmployeeNoteHandler(
    EmployeesDbContext dbContext,
    IClock clock,
    IAuditEventPublisher auditEventPublisher,
    IEmployeeTimelineWriter timelineWriter)
{
    public async Task<Result<CreateEmployeeNoteResponse>> HandleAsync(
        CreateEmployeeNoteRequest request,
        Guid actorEmployeeId,
        Guid actorUserId,
        CancellationToken cancellationToken)
    {
        var employeeExists = await dbContext.Employees
            .AnyAsync(e => e.CompanyId == request.CompanyId && e.Id == request.EmployeeId, cancellationToken);

        if (!employeeExists)
            return Result.Failure<CreateEmployeeNoteResponse>(
                Error.NotFound($"Employee '{request.EmployeeId}' was not found."));

        var now = clock.UtcNowOffset();

        var note = EmployeeNote.Create(
            Guid.NewGuid(),
            request.CompanyId,
            request.EmployeeId,
            request.Category,
            request.NoteText.Trim(),
            request.IsImportant,
            actorUserId,
            now);

        dbContext.EmployeeNotes.Add(note);
        await dbContext.SaveChangesAsync(cancellationToken);

        await auditEventPublisher.PublishAsync(
            new EmployeeNoteCreatedAuditEvent(
                request.CompanyId,
                request.EmployeeId,
                note.Id,
                note.Category.ToString(),
                note.IsImportant,
                actorUserId,
                actorEmployeeId,
                now),
            cancellationToken);

        // HR notes are confidential — the timeline entry must never carry the note's category or
        // text, only a fixed generic phrase, and must always be HrOnly regardless of the note's
        // own IsImportant/category values (see EmployeeTimelineVisibility doc comment).
        await timelineWriter.TryAddAsync(
            EmployeeTimelineEntry.Create(
                Guid.NewGuid(),
                request.CompanyId,
                request.EmployeeId,
                DateOnly.FromDateTime(now.DateTime),
                EmployeeTimelineEventType.HrNoteAdded,
                EmployeeTimelineCategory.HrNotes,
                "HR note added",
                "HR note added",
                actorUserId,
                "Employees",
                note.Id,
                EmployeeTimelineVisibility.HrOnly,
                now),
            cancellationToken);

        return Result.Success(new CreateEmployeeNoteResponse(
            note.Id,
            note.CompanyId,
            note.EmployeeId,
            note.Category.ToString(),
            note.NoteText,
            note.IsImportant,
            note.IsSuperseded,
            note.SupersededByNoteId,
            note.CreatedByUserId,
            note.CreatedDate));
    }
}
