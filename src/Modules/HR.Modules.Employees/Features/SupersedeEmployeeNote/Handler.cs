using HR.Infrastructure.Abstractions;
using HR.Modules.Employees.Domain;
using HR.Modules.Employees.Persistence;
using HR.SharedKernel;
using HR.SharedKernel.Idempotency;
using Microsoft.AspNetCore.Http;
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
        var scope = new IdempotencyScope(GetType().Name, request.CompanyId, Guid.Empty);

        var fingerprint = request.IdempotencyKey is not null
            ? DbContextIdempotencyExtensions.Fingerprint(request with { IdempotencyKey = null })
            : null;

        if (request.IdempotencyKey is { } precheckKey)
        {
            var replay = await dbContext.TryReplayAsync<IdempotencyRecord, SupersedeEmployeeNoteResponse>(scope, precheckKey, fingerprint!, cancellationToken);

            switch (replay?.Kind)
            {
                case IdempotencyOutcomeKind.Replayed:
                    return Result.Success(replay.Response!);
                case IdempotencyOutcomeKind.KeyReused:
                    return Result.Failure<SupersedeEmployeeNoteResponse>(
                        Error.Conflict("This Idempotency-Key was already used for a different request."));
            }
        }

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

        var response = new SupersedeEmployeeNoteResponse(
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
            original.IsSuperseded);

        if (request.IdempotencyKey is { } key)
        {
            var outcome = await dbContext.SaveIdempotentAsync<IdempotencyRecord, SupersedeEmployeeNoteResponse>(dbContext.IdempotencyRecords, 
                scope, key, fingerprint!, StatusCodes.Status201Created, response, now, cancellationToken);

            if (outcome.Kind == IdempotencyOutcomeKind.Replayed)
                return Result.Success(outcome.Response!);
        }
        else
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }

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

        return Result.Success(response);
    }
}
