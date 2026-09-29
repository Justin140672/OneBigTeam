using HR.Modules.Tasks.Contracts;
using HR.Infrastructure.Abstractions;
using HR.Modules.Companies.Contracts;
using HR.Modules.Documents.Domain;
using HR.Modules.Documents.Persistence;
using HR.Modules.Documents.Services;
using HR.SharedKernel;
using HR.SharedKernel.Idempotency;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Documents.Features.AcknowledgeSharedCompanyDocument;

internal sealed class AcknowledgeSharedCompanyDocumentHandler(
    DocumentsDbContext db,
    SharedCompanyDocumentAudienceMatcher audienceMatcher,
    ITaskCompleter taskCompleter,
    IAuditEventPublisher auditPublisher,
    ICompanyAcknowledgementSettingsReader companyAcknowledgementSettingsReader,
    IClock clock,
    IIntegrationEventPublisher integrationEventPublisher)
{
    public async Task<Result<AcknowledgeSharedCompanyDocumentResponse>> HandleAsync(
        AcknowledgeSharedCompanyDocumentRequest request,
        Guid callerEmployeeId,
        CancellationToken cancellationToken)
    {
        // Ticket 3 (P1) follow-up: dedupe a retried/duplicated request before doing any business
        // work, so a repeated delivery can't double-apply the acknowledgement.
                var scope = new IdempotencyScope(GetType().Name, request.CompanyId, Guid.Empty);
        

        var fingerprint = request.IdempotencyKey is not null
            ? DbContextIdempotencyExtensions.Fingerprint(request with { IdempotencyKey = null })
            : null;

        if (request.IdempotencyKey is { } precheckKey)
        {
            var replay = await db.TryReplayAsync<IdempotencyRecord, AcknowledgeSharedCompanyDocumentResponse>(scope, precheckKey, fingerprint!, cancellationToken);

            switch (replay?.Kind)
            {
                case IdempotencyOutcomeKind.Replayed:
                    return Result.Success(replay.Response!);
                case IdempotencyOutcomeKind.KeyReused:
                    return Result.Failure<AcknowledgeSharedCompanyDocumentResponse>(
                        Error.Conflict("This Idempotency-Key was already used for a different request."));
            }
        }

        var document = await db.SharedCompanyDocuments
            .AsNoTracking()
            .FirstOrDefaultAsync(
                d => d.Id == request.DocumentId &&
                     d.CompanyId == request.CompanyId &&
                     d.Status == SharedCompanyDocumentStatus.Published,
                cancellationToken);

        if (document is null)
            return Result.Failure<AcknowledgeSharedCompanyDocumentResponse>(
                Error.NotFound($"Shared document '{request.DocumentId}' was not found."));

        if (!document.RequiresAcknowledgement)
            return Result.Failure<AcknowledgeSharedCompanyDocumentResponse>(
                Error.Validation("This document does not require acknowledgement."));

        var inAudience = await audienceMatcher.IsEmployeeInAudienceAsync(
            request.CompanyId, document.Id, callerEmployeeId, cancellationToken);

        if (!inAudience)
            return Result.Failure<AcknowledgeSharedCompanyDocumentResponse>(
                Error.NotFound($"Shared document '{request.DocumentId}' was not found."));

        var now = clock.UtcNowOffset();

        // Idempotent: acknowledging the same version twice just returns the existing row rather
        // than creating a duplicate (the unique index on (document, employee, version) would
        // reject a second insert anyway, but this avoids the round-trip failure).
        var existing = await db.SharedCompanyDocumentAcknowledgements
            .FirstOrDefaultAsync(
                a => a.SharedCompanyDocumentId == document.Id &&
                     a.EmployeeId == callerEmployeeId &&
                     a.VersionNumber == document.VersionNumber,
                cancellationToken);

        if (existing is not null)
        {
            return Result.Success(new AcknowledgeSharedCompanyDocumentResponse(
                document.Id, document.VersionNumber, existing.AcknowledgedAt));
        }

        var acknowledgementStatement = string.IsNullOrWhiteSpace(document.AcknowledgementStatement)
            ? await companyAcknowledgementSettingsReader.GetDefaultAcknowledgementStatementAsync(request.CompanyId, cancellationToken)
            : document.AcknowledgementStatement;

        var acknowledgement = SharedCompanyDocumentAcknowledgement.Create(
            Guid.NewGuid(),
            request.CompanyId,
            document.Id,
            callerEmployeeId,
            document.VersionNumber,
            acknowledgementStatement,
            request.TaskId,
            request.Confirmed,
            now);

        db.SharedCompanyDocumentAcknowledgements.Add(acknowledgement);

        var response = new AcknowledgeSharedCompanyDocumentResponse(
            document.Id, document.VersionNumber, acknowledgement.AcknowledgedAt);

        if (request.IdempotencyKey is { } key)
        {
            var outcome = await db.SaveIdempotentAsync(db.IdempotencyRecords,
            scope, key, fingerprint!, StatusCodes.Status200OK, response, now, cancellationToken);

            if (outcome.Kind == IdempotencyOutcomeKind.Replayed)
                return Result.Success(outcome.Response!);
        }
        else
        {
            await db.SaveChangesAsync(cancellationToken);
        }

        await taskCompleter.CompleteBySourceEntityForEmployeeAsync(
            request.CompanyId,
            document.Id,
            TaskSource.Document,
            TaskActionType.Acknowledge,
            callerEmployeeId,
            callerEmployeeId,
            cancellationToken);

        await auditPublisher.PublishAsync(new SharedCompanyDocumentAcknowledgedAuditEvent(
            document.CompanyId,
            document.Id,
            document.Title,
            document.VersionNumber,
            callerEmployeeId,
            request.Confirmed,
            acknowledgementStatement,
            now), cancellationToken);

        await integrationEventPublisher.PublishAsync(
            new SharedCompanyDocumentAcknowledgedIntegrationEvent(
                document.CompanyId, callerEmployeeId, document.Id, document.Title, now),
            cancellationToken);

        return Result.Success(response);
    }
}
