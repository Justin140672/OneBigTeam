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

        // EmployeeTimelineVisibility's Wave 2/3 rules: HR notes must always be written as HrOnly
        // and with a generic Title/Summary — the actual note text/category must never appear on
        // the timeline, only in the Notes feature itself.
        await timelineWriter.TryAddAsync(
            EmployeeTimelineEntry.Create(
                Guid.NewGuid(),
                request.CompanyId,
                request.EmployeeId,
                DateOnly.FromDateTime(now.DateTime),
                EmployeeTimelineEventType.HrNoteAdded,
                EmployeeTimelineCategory.HrNotes,
                "HR note added",
                "An HR note was added to this employee's record.",
                performedByUserId: actorEmployeeId,
                "Employees",
                sourceRecordId: note.Id,
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
