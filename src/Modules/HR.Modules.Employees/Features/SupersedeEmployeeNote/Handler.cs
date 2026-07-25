using HR.Infrastructure.Abstractions;
using HR.Modules.Employees.Domain;
using HR.Modules.Employees.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Employees.Features.SupersedeEmployeeNote;

internal sealed class SupersedeEmployeeNoteHandler(
    EmployeesDbContext dbContext,
    IClock clock,
    IAuditEventPublisher auditEventPublisher)
{
    public async Task<Result<SupersedeEmployeeNoteResponse>> HandleAsync(
        SupersedeEmployeeNoteRequest request,
        Guid actorEmployeeId,
        Guid actorUserId,
        CancellationToken cancellationToken)
    {
        var original = await dbContext.EmployeeNotes
            .SingleOrDefaultAsync(
                n => n.CompanyId == request.CompanyId &&
                     n.EmployeeId == request.EmployeeId &&
                     n.Id == request.OriginalNoteId,
                cancellationToken);

        if (original is null)
            return Result.Failure<SupersedeEmployeeNoteResponse>(
                Error.NotFound($"Employee note '{request.OriginalNoteId}' was not found."));

        if (original.IsSuperseded)
            return Result.Failure<SupersedeEmployeeNoteResponse>(
                Error.Conflict($"Employee note '{request.OriginalNoteId}' has already been superseded."));

        var now = clock.UtcNowOffset();

        var newNote = EmployeeNote.Create(
            Guid.NewGuid(),
            request.CompanyId,
            request.EmployeeId,
            request.Category,
            request.NoteText.Trim(),
            request.IsImportant,
            actorUserId,
            now);

        original.MarkSuperseded(newNote.Id);

        dbContext.EmployeeNotes.Add(newNote);
        await dbContext.SaveChangesAsync(cancellationToken);

        await auditEventPublisher.PublishAsync(
            new EmployeeNoteCreatedAuditEvent(
                request.CompanyId,
                request.EmployeeId,
                newNote.Id,
                newNote.Category.ToString(),
                newNote.IsImportant,
                actorUserId,
                actorEmployeeId,
                now),
            cancellationToken);

        await auditEventPublisher.PublishAsync(
            new EmployeeNoteSupersededAuditEvent(
                request.CompanyId,
                request.EmployeeId,
                original.Id,
                newNote.Id,
                actorUserId,
                actorEmployeeId,
                now),
            cancellationToken);

        return Result.Success(new SupersedeEmployeeNoteResponse(
            newNote.Id,
            newNote.CompanyId,
            newNote.EmployeeId,
            newNote.Category.ToString(),
            newNote.NoteText,
            newNote.IsImportant,
            newNote.IsSuperseded,
            newNote.SupersededByNoteId,
            newNote.CreatedByUserId,
            newNote.CreatedDate,
            original.Id,
            original.IsSuperseded));
    }
}
