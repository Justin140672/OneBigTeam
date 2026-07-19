using HR.Infrastructure.Abstractions;
using HR.Modules.Documents.Domain;
using HR.Modules.Documents.Persistence;
using HR.Modules.Documents.Services;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Documents.Features.AcknowledgeSharedCompanyDocument;

internal sealed class AcknowledgeSharedCompanyDocumentHandler(
    DocumentsDbContext db,
    SharedCompanyDocumentAudienceMatcher audienceMatcher,
    ITaskCompleter taskCompleter,
    IAuditEventPublisher auditPublisher,
    ICompanyAcknowledgementSettingsReader companyAcknowledgementSettingsReader,
    IClock clock)
{
    public async Task<Result<AcknowledgeSharedCompanyDocumentResponse>> HandleAsync(
        AcknowledgeSharedCompanyDocumentRequest request,
        Guid callerEmployeeId,
        CancellationToken cancellationToken)
    {
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
        await db.SaveChangesAsync(cancellationToken);

        // Complete the acknowledging employee's own open Acknowledge task for this document, if
        // one exists — scoped to this employee specifically (not just "the first open task for
        // this document") since a published document fans out to one task per eligible employee.
        // Covers both entry paths: acknowledging via the task itself, and browsing directly to
        // the document (e.g. via My Documents) while a task is still outstanding.
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

        return Result.Success(new AcknowledgeSharedCompanyDocumentResponse(
            document.Id, document.VersionNumber, acknowledgement.AcknowledgedAt));
    }
}
