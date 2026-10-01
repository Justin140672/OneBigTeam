using HR.Modules.Recruitment.Domain;
using HR.Modules.Recruitment.Persistence;
using HR.Modules.Recruitment.Services;
using HR.SharedKernel;
using HR.SharedKernel.ExecutionContext;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HR.Modules.Recruitment.Features.UploadCandidateDocument;

/// <summary>
/// Security/code review finding #3 (superseded — see follow-up below): compensates using an
/// independently-bounded cleanup token, never the request token, and logs a compensation failure with
/// correlation context and a redacted storage key.
///
/// Follow-up review finding: durable intent is now written BEFORE the upload, not only as
/// compensation after a failure is detected. A durable <see cref="CandidateDocumentDeletionOperation"/>
/// row (status Reserved) is persisted for the final storage key before any bytes are uploaded. If the
/// document save succeeds, that row is confirmed in the SAME SaveChangesAsync call. If anything fails
/// afterward — including a process crash, or the DB being unreachable for both the document save AND
/// the compensating delete — the Reserved row already durably exists, so
/// PurgeCandidateDocumentStorageReconciliationJob's intent sweep (see that job's remarks) is the
/// authoritative backstop: after a grace period it checks whether the object actually exists in
/// storage and either hands it to the existing Pending claim/delete pipeline or clears it if nothing
/// was ever uploaded. "Log an unrecoverable orphan and give up" is no longer a terminal outcome here.
///
/// Internal recruitment Ticket 3: the reserve/upload/compensate sequence now lives in
/// <see cref="CandidateDocumentUploadStaging"/> so the coordinated CreateCandidateApplication intake
/// reuses exactly the same guarantees. Behaviour of this handler is unchanged.
/// </summary>
internal sealed class UploadCandidateDocumentHandler(
    RecruitmentDbContext db,
    ICandidateDocumentStorageService storage,
    IOptions<CandidateDocumentUploadOptions> options,
    IClock clock,
    ILogger<UploadCandidateDocumentHandler> logger,
    IExecutionContextAccessor? executionContextAccessor = null)
{
    public async Task<Result<UploadCandidateDocumentResponse>> HandleAsync(
        UploadCandidateDocumentRequest request,
        Guid uploadedBy,
        CancellationToken cancellationToken)
    {
        var candidate = await db.Candidates
            .AsNoTracking()
            .Where(c => c.Id == request.CandidateId && c.CompanyId == request.CompanyId)
            .Select(c => new { c.PurgedAt })
            .SingleOrDefaultAsync(cancellationToken);

        if (candidate is null)
            return Result.Failure<UploadCandidateDocumentResponse>(
                Error.NotFound($"Candidate '{request.CandidateId}' was not found."));

        // Ticket 7 (P2): a purged candidate's documents/personal data were removed by an explicit,
        // separately-authorised retention action — a new upload must never be able to repopulate them.
        if (candidate.PurgedAt is not null)
            return Result.Failure<UploadCandidateDocumentResponse>(
                Error.Conflict("This candidate's data has been purged under the retention policy and can no longer accept new documents."));

        var file = request.File;
        var kind = Enum.TryParse<CandidateDocumentKind>(request.Kind, ignoreCase: true, out var parsedKind)
            ? parsedKind
            : CandidateDocumentKind.Other;

        var validationResult = kind == CandidateDocumentKind.Cv
            ? await CandidateDocumentUploadStaging.ValidateCvFileAsync(file, options.Value, cancellationToken)
            : CandidateDocumentUploadStaging.ValidateFile(file.FileName, file.ContentType, file.Length, options.Value);
        if (validationResult.IsFailure)
            return Result.Failure<UploadCandidateDocumentResponse>(validationResult.Error);

        var staging = new CandidateDocumentUploadStaging(db, storage, clock, logger, executionContextAccessor);
        var staged = await staging.ReserveAndUploadAsync(request.CompanyId, request.CandidateId, file, cancellationToken);

        var now = clock.UtcNowOffset();

        var document = CandidateDocument.Create(
            Guid.NewGuid(),
            request.CompanyId,
            request.CandidateId,
            request.Title,
            staged.FileName,
            staged.FileSize,
            staged.ContentType,
            staged.StorageKey,
            uploadedBy,
            now,
            kind);

        db.CandidateDocuments.Add(document);
        staged.Intent.MarkConfirmed(now);

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch
        {
            db.ChangeTracker.Clear();
            await staging.CompensateAsync(staged);
            throw;
        }

        return Result.Success(new UploadCandidateDocumentResponse(
            document.Id,
            document.CompanyId,
            document.CandidateId,
            document.Title,
            document.Kind.ToString(),
            document.FileName,
            document.FileSize,
            document.ContentType,
            document.CreatedAt));
    }

    internal static string RedactStorageKey(string storageKey) =>
        CandidateDocumentUploadStaging.RedactStorageKey(storageKey);
}
